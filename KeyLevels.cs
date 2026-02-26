// KeyLevels.cs — NinjaTrader 8 Indicator
// Ported from SpacemanBTC Key Level (Pine Script).
//
// Features:
//   • Daily / Weekly / Monthly / Yearly Opens + Prev H/L
//   • Monday Range
//   • Optional London / New York / Asia session H/L and Open (separately toggled)
//   • Global Color Override — one checkbox/color to paint every level the same
//   • All colors default to slate gray
//
// SESSION TIME NOTES:
//   Comparisons use HHMMSS integers from bar Time[0] (UTC by default):
//     London   08:00–16:00  →  80000–160000
//     New York 13:30–20:00  →  133000–200000
//     Asia     00:00–09:00  →  0–90000
//   Adjust the constants inside UpdateSessions() if your feed uses a different timezone.
//
// INSTALL:
//   Tools → Edit NinjaScript → Indicators → right-click → New → name "KeyLevels"
//   Paste entire file, press F5 to compile.
// ---------------------------------------------------------------------------

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    [CategoryOrder("Global",     0)]
    [CategoryOrder("Visibility", 1)]
    [CategoryOrder("Sessions",   2)]
    [CategoryOrder("Colors",     3)]
    [CategoryOrder("Display",    4)]
    public class KeyLevels : Indicator
    {
        // Slate gray used as the universal default
        private static readonly Brush SlateGray = Brushes.SlateGray;

        // ── HTF snapshot values ─────────────────────────────────────────
        private double _dO,  _pdH,  _pdL;
        private double _wO,  _pwH,  _pwL;
        private double _mO,  _pmH,  _pmL;
        private double _yO,  _cyH,  _cyL;
        private double _monH, _monL;

        private int _dBar, _wBar, _mBar, _yBar, _monBar;

        private int      _lastDayIdx   = -1, _lastWeekIdx  = -1;
        private int      _lastMonthIdx = -1, _lastYearIdx  = -1;
        private DateTime _lastMondayDate = DateTime.MinValue;

        // ── Live session accumulators ───────────────────────────────────
        private double _lonH, _lonL, _lonO;
        private double _nyH,  _nyL,  _nyO;
        private double _asH,  _asL,  _asO;
        private bool   _lonActive, _nyActive, _asActive;
        private int    _lonBar,    _nyBar,    _asBar;

        // Last completed session snapshots
        private double _sLonH, _sLonL, _sLonO;
        private double _sNyH,  _sNyL,  _sNyO;
        private double _sAsH,  _sAsL,  _sAsO;
        private int    _sLonBar, _sNyBar, _sAsBar;

        // ── Life-cycle ──────────────────────────────────────────────────
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name             = "Key Levels";
                Description      = "Multi-timeframe key price levels";
                Calculate        = Calculate.OnBarClose;
                IsOverlay        = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;

                // Global override
                UseGlobalColor  = false;
                GlobalColor     = SlateGray;

                // Visibility
                ShowDailyOpen     = true;
                ShowPrevDayHL     = true;
                ShowWeeklyOpen    = true;
                ShowPrevWeekHL    = true;
                ShowMonthlyOpen   = true;
                ShowPrevMonthHL   = true;
                ShowYearlyOpen    = true;
                ShowCurrentYearHL = false;
                ShowMondayRange   = true;

                // Sessions
                ShowLondonHL      = false;
                ShowLondonOpen    = false;
                ShowNYHL          = false;
                ShowNYOpen        = false;
                ShowAsiaHL        = false;
                ShowAsiaOpen      = false;

                // Colors — all slate gray by default
                DailyColor   = SlateGray;
                WeeklyColor  = SlateGray;
                MonthlyColor = SlateGray;
                YearlyColor  = SlateGray;
                MondayColor  = SlateGray;
                LondonColor  = SlateGray;
                NYColor      = SlateGray;
                AsiaColor    = SlateGray;

                // Display
                LineWidth  = 1;
                ExtendBars = 30;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Day,   1);    // [1] Daily
                AddDataSeries(BarsPeriodType.Week,  1);    // [2] Weekly
                AddDataSeries(BarsPeriodType.Month, 1);    // [3] Monthly
                AddDataSeries(BarsPeriodType.Month, 12);   // [4] Yearly (12M)
            }
        }

        // ── Main loop ───────────────────────────────────────────────────
        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;
            if (CurrentBar      < 2) return;

            UpdateDailyData();
            UpdateWeeklyData();
            UpdateMonthlyData();
            UpdateYearlyData();
            UpdateMondayRange();
            UpdateSessions();
            DrawAllLevels();
        }

        // ── Helpers ─────────────────────────────────────────────────────
        // Returns the resolved color — global override if enabled, else perLevel color.
        private Brush C(Brush perLevel) => UseGlobalColor ? GlobalColor : perLevel;

        // ── HTF data refresh ────────────────────────────────────────────
        private void UpdateDailyData()
        {
            if (BarsArray[1] == null || CurrentBars[1] < 1) return;
            if (CurrentBars[1] != _lastDayIdx)
            {
                _lastDayIdx = CurrentBars[1];
                _dBar = CurrentBar;
            }
            _dO  = Opens[1][0];
            _pdH = Highs[1][1];
            _pdL = Lows[1][1];
        }

        private void UpdateWeeklyData()
        {
            if (BarsArray[2] == null || CurrentBars[2] < 1) return;
            if (CurrentBars[2] != _lastWeekIdx)
            {
                _lastWeekIdx = CurrentBars[2];
                _wBar = CurrentBar;
            }
            _wO  = Opens[2][0];
            _pwH = Highs[2][1];
            _pwL = Lows[2][1];
        }

        private void UpdateMonthlyData()
        {
            if (BarsArray[3] == null || CurrentBars[3] < 1) return;
            if (CurrentBars[3] != _lastMonthIdx)
            {
                _lastMonthIdx = CurrentBars[3];
                _mBar = CurrentBar;
            }
            _mO  = Opens[3][0];
            _pmH = Highs[3][1];
            _pmL = Lows[3][1];
        }

        private void UpdateYearlyData()
        {
            if (BarsArray[4] == null || CurrentBars[4] < 0) return;
            if (CurrentBars[4] != _lastYearIdx)
            {
                _lastYearIdx = CurrentBars[4];
                _yBar = CurrentBar;
            }
            _yO  = Opens[4][0];
            _cyH = Highs[4][0];
            _cyL = Lows[4][0];
        }

        private void UpdateMondayRange()
        {
            if (!ShowMondayRange) return;
            if (Time[0].DayOfWeek != DayOfWeek.Monday) return;

            DateTime today = Time[0].Date;
            if (today != _lastMondayDate)
            {
                _lastMondayDate = today;
                _monH   = High[0];
                _monL   = Low[0];
                _monBar = CurrentBar;
            }
            else
            {
                if (High[0] > _monH) _monH = High[0];
                if (Low[0]  < _monL) _monL = Low[0];
            }
        }

        // ── Session tracking ────────────────────────────────────────────
        private void UpdateSessions()
        {
            bool anySession = ShowLondonHL || ShowLondonOpen
                           || ShowNYHL    || ShowNYOpen
                           || ShowAsiaHL  || ShowAsiaOpen;
            if (!anySession) return;

            int t = ToTime(Time[0]);

            // London 08:00–16:00 UTC
            if (ShowLondonHL || ShowLondonOpen)
            {
                bool inSession = (t >= 80000 && t < 160000);
                if (inSession && !_lonActive)
                {
                    _lonActive = true;
                    _lonO = Open[0]; _lonH = High[0]; _lonL = Low[0];
                    _lonBar = CurrentBar;
                }
                else if (inSession)
                {
                    if (High[0] > _lonH) _lonH = High[0];
                    if (Low[0]  < _lonL) _lonL = Low[0];
                }
                else if (!inSession && _lonActive)
                {
                    _sLonH = _lonH; _sLonL = _lonL; _sLonO = _lonO;
                    _sLonBar = _lonBar;
                    _lonActive = false;
                }
            }

            // New York 13:30–20:00 UTC
            if (ShowNYHL || ShowNYOpen)
            {
                bool inSession = (t >= 133000 && t < 200000);
                if (inSession && !_nyActive)
                {
                    _nyActive = true;
                    _nyO = Open[0]; _nyH = High[0]; _nyL = Low[0];
                    _nyBar = CurrentBar;
                }
                else if (inSession)
                {
                    if (High[0] > _nyH) _nyH = High[0];
                    if (Low[0]  < _nyL) _nyL = Low[0];
                }
                else if (!inSession && _nyActive)
                {
                    _sNyH = _nyH; _sNyL = _nyL; _sNyO = _nyO;
                    _sNyBar = _nyBar;
                    _nyActive = false;
                }
            }

            // Asia 00:00–09:00 UTC
            if (ShowAsiaHL || ShowAsiaOpen)
            {
                bool inSession = (t >= 0 && t < 90000);
                if (inSession && !_asActive)
                {
                    _asActive = true;
                    _asO = Open[0]; _asH = High[0]; _asL = Low[0];
                    _asBar = CurrentBar;
                }
                else if (inSession)
                {
                    if (High[0] > _asH) _asH = High[0];
                    if (Low[0]  < _asL) _asL = Low[0];
                }
                else if (!inSession && _asActive)
                {
                    _sAsH = _asH; _sAsL = _asL; _sAsO = _asO;
                    _sAsBar = _asBar;
                    _asActive = false;
                }
            }
        }

        // ── Drawing ─────────────────────────────────────────────────────
        private void DrawAllLevels()
        {
            bool isIntraday = Bars.BarsType.IsIntraday;

            // Daily (intraday charts only)
            if (isIntraday)
            {
                if (ShowDailyOpen && _dO > 0)
                    DrawLevel("DO",  _dO,  _dBar, C(DailyColor), "Daily Open");
                if (ShowPrevDayHL && _pdH > 0)
                {
                    DrawLevel("PDH", _pdH, _dBar, C(DailyColor), "Prev Day High");
                    DrawLevel("PDL", _pdL, _dBar, C(DailyColor), "Prev Day Low");
                }
            }

            // Weekly
            if (ShowWeeklyOpen && _wO > 0)
                DrawLevel("WO",  _wO,  _wBar, C(WeeklyColor), "Weekly Open");
            if (ShowPrevWeekHL && _pwH > 0)
            {
                DrawLevel("PWH", _pwH, _wBar, C(WeeklyColor), "Prev Week High");
                DrawLevel("PWL", _pwL, _wBar, C(WeeklyColor), "Prev Week Low");
            }

            // Monthly
            if (ShowMonthlyOpen && _mO > 0)
                DrawLevel("MO",  _mO,  _mBar, C(MonthlyColor), "Monthly Open");
            if (ShowPrevMonthHL && _pmH > 0)
            {
                DrawLevel("PMH", _pmH, _mBar, C(MonthlyColor), "Prev Month High");
                DrawLevel("PML", _pmL, _mBar, C(MonthlyColor), "Prev Month Low");
            }

            // Yearly
            if (ShowYearlyOpen && _yO > 0)
                DrawLevel("YO",  _yO,  _yBar, C(YearlyColor), "Yearly Open");
            if (ShowCurrentYearHL && _cyH > 0)
            {
                DrawLevel("CYH", _cyH, _yBar, C(YearlyColor), "Curr Year High");
                DrawLevel("CYL", _cyL, _yBar, C(YearlyColor), "Curr Year Low");
            }

            // Monday Range
            if (ShowMondayRange && _monH > 0)
            {
                DrawLevel("MonH", _monH, _monBar, C(MondayColor), "Monday High");
                DrawLevel("MonL", _monL, _monBar, C(MondayColor), "Monday Low");
            }

            // London
            if (_sLonH > 0)
            {
                if (ShowLondonHL)
                {
                    DrawLevel("LonH", _sLonH, _sLonBar, C(LondonColor), "London High");
                    DrawLevel("LonL", _sLonL, _sLonBar, C(LondonColor), "London Low");
                }
                if (ShowLondonOpen)
                    DrawLevel("LonO", _sLonO, _sLonBar, C(LondonColor), "London Open");
            }

            // New York
            if (_sNyH > 0)
            {
                if (ShowNYHL)
                {
                    DrawLevel("NYH", _sNyH, _sNyBar, C(NYColor), "NY High");
                    DrawLevel("NYL", _sNyL, _sNyBar, C(NYColor), "NY Low");
                }
                if (ShowNYOpen)
                    DrawLevel("NYO", _sNyO, _sNyBar, C(NYColor), "NY Open");
            }

            // Asia
            if (_sAsH > 0)
            {
                if (ShowAsiaHL)
                {
                    DrawLevel("AsH", _sAsH, _sAsBar, C(AsiaColor), "Asia High");
                    DrawLevel("AsL", _sAsL, _sAsBar, C(AsiaColor), "Asia Low");
                }
                if (ShowAsiaOpen)
                    DrawLevel("AsO", _sAsO, _sAsBar, C(AsiaColor), "Asia Open");
            }
        }

        private void DrawLevel(string tag, double price, int startBar,
                                Brush color, string labelText)
        {
            int barsBack = Math.Max(0, CurrentBar - startBar);

            Draw.Line(this, tag, true,
                barsBack, price,
                -ExtendBars, price,
                color, DashStyleHelper.Solid, LineWidth);

            Draw.Text(this, tag + "_lbl", true,
                labelText,
                -ExtendBars + 1, price, 0,
                color,
                new SimpleFont("Arial", 10),
                System.Windows.TextAlignment.Left,
                Brushes.Transparent, Brushes.Transparent, 0);
        }

        // ── Properties ──────────────────────────────────────────────────

        #region Global
        [NinjaScriptProperty]
        [Display(Name = "Use Global Color", GroupName = "Global", Order = 1,
                 Description = "When enabled, all levels use the single color below instead of their individual colors.")]
        public bool UseGlobalColor { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Global Color", GroupName = "Global", Order = 2)]
        public Brush GlobalColor { get; set; }
        [Browsable(false)]
        public string GlobalColorSerialize
        { get { return Serialize.BrushToString(GlobalColor); } set { GlobalColor = Serialize.StringToBrush(value); } }
        #endregion

        #region Visibility
        [NinjaScriptProperty]
        [Display(Name = "Daily Open",        GroupName = "Visibility", Order = 1)]
        public bool ShowDailyOpen { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Prev Day H/L",      GroupName = "Visibility", Order = 2)]
        public bool ShowPrevDayHL { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Weekly Open",       GroupName = "Visibility", Order = 3)]
        public bool ShowWeeklyOpen { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Prev Week H/L",     GroupName = "Visibility", Order = 4)]
        public bool ShowPrevWeekHL { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Monthly Open",      GroupName = "Visibility", Order = 5)]
        public bool ShowMonthlyOpen { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Prev Month H/L",    GroupName = "Visibility", Order = 6)]
        public bool ShowPrevMonthHL { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Yearly Open",       GroupName = "Visibility", Order = 7)]
        public bool ShowYearlyOpen { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Current Year H/L",  GroupName = "Visibility", Order = 8)]
        public bool ShowCurrentYearHL { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Monday Range H/L",  GroupName = "Visibility", Order = 9)]
        public bool ShowMondayRange { get; set; }
        #endregion

        #region Sessions
        [NinjaScriptProperty]
        [Display(Name = "London H/L  (08:00–16:00 UTC)",  GroupName = "Sessions", Order = 1)]
        public bool ShowLondonHL { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London Open",                    GroupName = "Sessions", Order = 2)]
        public bool ShowLondonOpen { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York H/L  (13:30–20:00 UTC)", GroupName = "Sessions", Order = 3)]
        public bool ShowNYHL { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York Open",                   GroupName = "Sessions", Order = 4)]
        public bool ShowNYOpen { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Asia H/L  (00:00–09:00 UTC)",    GroupName = "Sessions", Order = 5)]
        public bool ShowAsiaHL { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Asia Open",                      GroupName = "Sessions", Order = 6)]
        public bool ShowAsiaOpen { get; set; }
        #endregion

        #region Colors
        [NinjaScriptProperty][XmlIgnore]
        [Display(Name = "Daily",   GroupName = "Colors", Order = 1)]
        public Brush DailyColor { get; set; }
        [Browsable(false)]
        public string DailyColorSerialize
        { get { return Serialize.BrushToString(DailyColor); } set { DailyColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty][XmlIgnore]
        [Display(Name = "Weekly",  GroupName = "Colors", Order = 2)]
        public Brush WeeklyColor { get; set; }
        [Browsable(false)]
        public string WeeklyColorSerialize
        { get { return Serialize.BrushToString(WeeklyColor); } set { WeeklyColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty][XmlIgnore]
        [Display(Name = "Monthly", GroupName = "Colors", Order = 3)]
        public Brush MonthlyColor { get; set; }
        [Browsable(false)]
        public string MonthlyColorSerialize
        { get { return Serialize.BrushToString(MonthlyColor); } set { MonthlyColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty][XmlIgnore]
        [Display(Name = "Yearly",  GroupName = "Colors", Order = 4)]
        public Brush YearlyColor { get; set; }
        [Browsable(false)]
        public string YearlyColorSerialize
        { get { return Serialize.BrushToString(YearlyColor); } set { YearlyColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty][XmlIgnore]
        [Display(Name = "Monday",  GroupName = "Colors", Order = 5)]
        public Brush MondayColor { get; set; }
        [Browsable(false)]
        public string MondayColorSerialize
        { get { return Serialize.BrushToString(MondayColor); } set { MondayColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty][XmlIgnore]
        [Display(Name = "London",  GroupName = "Colors", Order = 6)]
        public Brush LondonColor { get; set; }
        [Browsable(false)]
        public string LondonColorSerialize
        { get { return Serialize.BrushToString(LondonColor); } set { LondonColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty][XmlIgnore]
        [Display(Name = "New York", GroupName = "Colors", Order = 7)]
        public Brush NYColor { get; set; }
        [Browsable(false)]
        public string NYColorSerialize
        { get { return Serialize.BrushToString(NYColor); } set { NYColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty][XmlIgnore]
        [Display(Name = "Asia",    GroupName = "Colors", Order = 8)]
        public Brush AsiaColor { get; set; }
        [Browsable(false)]
        public string AsiaColorSerialize
        { get { return Serialize.BrushToString(AsiaColor); } set { AsiaColor = Serialize.StringToBrush(value); } }
        #endregion

        #region Display
        [NinjaScriptProperty]
        [Range(1, 5)]
        [Display(Name = "Line Width",        GroupName = "Display", Order = 1)]
        public int LineWidth { get; set; }

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "Extend Bars Right", GroupName = "Display", Order = 2)]
        public int ExtendBars { get; set; }
        #endregion
    }
}

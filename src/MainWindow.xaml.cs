using System.Data;
using System.IO.Compression;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace FunctionExplorer;

public partial class MainWindow : Window
{
    private sealed record Topic(
        string Id, string Name, string Formula, string Course, string[] Grades, string Hint,
        Func<double, double, double, double, double> Eval,
        string A, string B, string C, double DefaultA = 1, double DefaultB = 0, double DefaultC = 0);
    private sealed record Scenario(string Name, string TopicId, string Grade, string Description, string Question, string Goal, double A, double B, double C, string Min, string Max, string XUnit, string YUnit);
    private sealed record ActivityEvent(DateTime At, string TopicId, string Action, double A, double B, double C, string? Detail, bool HasProbeA, bool HasProbeB, double ProbeA, double ProbeB, double Zoom, double ZoomY, double PanX, double PanY, string RangeMinimum, string RangeMaximum);
    private sealed record StudentSubmission(string Grade,string Topic,string Question,string Prediction,string Observations,string Explanation,string Reflection,bool[] Rubric,DateTime SavedAt,string SourceFile,string SourcePath)
    { public int SelfAssessment=>Rubric.Count(x=>x); }
    private sealed class ParameterComparison
    {
        public bool Enabled { get; set; }
        public double A { get; set; }
        public double B { get; set; }
        public double C { get; set; }
        public string Name { get; set; } = "比較";
        public int ColorIndex { get; set; }
    }
    private sealed class LessonPreset
    {
        public int Version { get; set; } = 1;
        public string TopicId { get; set; } = "proportion";
        public string Grade { get; set; } = "小学校6年";
        public double A { get; set; } = 1;
        public double B { get; set; }
        public double C { get; set; }
        public string? CompareTopicId { get; set; }
        public string[] ComparisonTopicIds { get; set; } = Array.Empty<string>();
        public string Question { get; set; } = "";
        public string Goal { get; set; } = "";
        public string Prediction { get; set; } = "";
        public string Evidence { get; set; } = "";
        public string Explanation { get; set; } = "";
        public string Notes { get; set; } = "";
        public string RangeMinimum { get; set; } = "-10";
        public string RangeMaximum { get; set; } = "10";
        public bool IncludeMinimum { get; set; } = true;
        public bool IncludeMaximum { get; set; } = true;
        public int RangeStyle { get; set; }
        public string LessonMinutes { get; set; } = "50";
        public string StudentResponses { get; set; } = "";
        public string SupportAndAssessment { get; set; } = "";
        public string Reflection { get; set; } = "";
        public string JournalContents { get; set; } = "";
        public List<ParameterComparison> ParameterComparisons { get; set; } = new();
        public string? ScenarioName { get; set; }
        public string Tags { get; set; } = "";
        public bool Favorite { get; set; }
        public string StudentPrediction { get; set; } = "";
        public string StudentObservation { get; set; } = "";
        public string StudentExplanation { get; set; } = "";
        public string StudentReflection { get; set; } = "";
        public bool[] Rubric { get; set; } = Array.Empty<bool>();
        public List<ActivityEvent> ActivityLog { get; set; } = new();
        public double[] TableXValues { get; set; } = Array.Empty<double>();
        public string[] RecentComparisonIds { get; set; } = Array.Empty<string>();
        public double ViewZoom { get; set; } = 1;
        public double ViewZoomY { get; set; } = 1;
        public double ViewPanX { get; set; }
        public double ViewPanY { get; set; }
    }

    private readonly string[] grades = { "すべての学年", "小学校6年", "中学1年", "中学2年", "中学3年", "高等学校 数学I", "高等学校 数学II" };
    private readonly List<Scenario> scenarios = new()
    {
        new("自転車で走る距離｜比例", "proportion", "小学校6年", "x は30分単位、y は走った距離（km）。30分ごとに4 km進むとします。", "時間が2倍、3倍になると、走った距離はどうなる？", "時間と距離の比例関係を、式・表・グラフで説明する。", 4, 0, 0, "0", "5", "30分", "km"),
        new("配車サービスの料金｜一次関数", "linear", "中学2年", "x は走行距離（km）、y は料金（千円）。初乗り1,000円、1 kmごとに500円とします。", "グラフの傾きと切片は、料金のどの部分を表している？", "傾きと切片を、料金の変化と初乗り料金に結び付けて説明する。", 0.5, 1, 0, "0", "20", "km", "千円"),
        new("正方形の面積｜二次関数", "quadratic-jhs", "中学3年", "x は正方形の辺の長さ（m）、y は面積（m²）。辺の長さは0以上です。", "辺の長さを2倍にすると面積は何倍？表とグラフで確かめよう。", "辺の長さと面積の関係が比例ではないことを、式・表・グラフで説明する。", 1, 0, 0, "0", "8", "m", "m²"),
        new("一定割合で増える量｜指数関数", "exponential", "高等学校 数学II", "x は経過した期間、y は最初の量を1とした相対量。1期間ごとに1.5倍とします。", "期間ごとの増え方は、一定の差と一定の倍率のどちらで表せる？", "指数関数の底を、一定期間ごとの倍率として説明する。", 1.5, 0, 0, "0", "8", "期間", "相対量")
    };
    private readonly List<Topic> topics = new()
    {
        new("proportion", "比例", "y = ax", "小学校算数・中学1年", new[]{"小学校6年", "中学1年"}, "a を変えると、直線の傾きはどう変わる？原点を通る理由も説明してみよう。", (x,a,b,c)=>a*x, "比例定数 a", "—", "—", 1),
        new("inverse", "反比例", "y = a / x（x ≠ 0）", "中学1年", new[]{"中学1年"}, "x と y の積はいつも一定？グラフの形とあわせて確かめよう。", (x,a,b,c)=>Math.Abs(x)<1e-9?double.NaN:a/x, "比例定数 a", "—", "—", 4),
        new("linear", "一次関数", "y = ax + b", "中学2年", new[]{"中学2年"}, "a と b はグラフのどの特徴を決めている？値を変えて比較しよう。", (x,a,b,c)=>a*x+b, "傾き a", "切片 b", "—", 1, 2),
        new("quadratic-jhs", "関数 y = ax²", "y = ax^2", "中学3年", new[]{"中学3年"}, "a の符号や大きさを変えると、放物線の開き方はどう変わる？", (x,a,b,c)=>a*x*x, "係数 a", "—", "—", 1),
        new("quadratic-hs", "二次関数", "y = a(x − h)^2 + k", "高等学校 数学I", new[]{"高等学校 数学I"}, "a・h・k をそれぞれ変え、頂点・軸・開き方がどう移るか説明しよう。", (x,a,h,k)=>a*Math.Pow(x-h,2)+k, "係数 a", "頂点の x 座標 h", "頂点の y 座標 k", 1, 0, 0),
        new("exponential", "指数関数", "y = a^x", "高等学校 数学II", new[]{"高等学校 数学II"}, "底 a を変えると増加・減少はどう切り替わる？同じ x の変化で比べよう。", (x,a,b,c)=>Math.Pow(a,x), "底 a（a > 0）", "—", "—", 2),
        new("logarithm", "対数関数", "y = log_a x（a > 0, a ≠ 1; x > 0）", "高等学校 数学II", new[]{"高等学校 数学II"}, "底 a と x の範囲を確認し、指数関数との関係をグラフで説明しよう。", (x,a,b,c)=>x<=0||a<=0||Math.Abs(a-1)<1e-9?double.NaN:Math.Log(x,a), "底 a（a > 0, a ≠ 1）", "—", "—", 2),
        new("trigonometric", "三角関数", "y = a sin(bx)（x はラジアン）", "高等学校 数学II", new[]{"高等学校 数学II"}, "振幅 a と周期を決める b の役割を、山と谷の位置から説明しよう。", (x,a,b,c)=>a*Math.Sin(b*x), "振幅 a", "角の係数 b", "—", 1, 1),
    };
    private bool initializing;
    private bool loadingScenario;
    private readonly Stack<LessonPreset> undoStack=new();
    private LessonPreset? lastTrackedState;
    private LessonPreset? undoPendingState;
    private readonly DispatcherTimer undoTimer=new(){Interval=TimeSpan.FromMilliseconds(700)};
    private readonly DispatcherTimer autosaveTimer=new(){Interval=TimeSpan.FromMinutes(1)};
    private readonly DispatcherTimer lessonTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private TimeSpan lessonRemaining=TimeSpan.FromMinutes(50);
    private int activeLessonStep;
    private string DataDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FunctionExplorer");
    private double plotZoom=1, plotZoomY=1, plotPanX, plotPanY, graphXMin, graphXMax, graphYMin, graphYMax;
    private Point dragStart;
    private bool isPlotDragging;
    private bool coordinatePinned;
    private bool toolsPaneCollapsed;
    private bool presentationMode;
    private bool previousToolsPaneCollapsed;
    private bool previousStudentMode;
    private bool previousTableDockOpen;
    private WindowState previousWindowState;
    private WindowStyle previousWindowStyle;
    private bool replayingActivity;
    private int replayIndex;
    private readonly List<ActivityEvent> activityLog = new();
    private List<double> tableXValues = new();
    private readonly DispatcherTimer replayTimer = new(){Interval=TimeSpan.FromMilliseconds(550)};
    private readonly List<Scenario> customScenarios = new();
    private string? currentLessonPath;
    private string? lastProbeText;
    private bool probeASet;
    private double probeAx, probeAy, probeBx, probeBy;
    private bool probeBSet, plotMoved;
    private Point plotDownPoint;
    private Topic Current => FunctionList.SelectedItem as Topic ?? topics[0];
    private IEnumerable<Topic> SelectedComparisons => ComparisonList is null
        ? Enumerable.Empty<Topic>()
        : ComparisonList.SelectedItems.Cast<Topic>().Where(t => FunctionList.SelectedItem is not Topic current || t.Id != current.Id);

    public MainWindow()
    {
        InitializeComponent();
        initializing = true;
        GradeBox.ItemsSource = grades;
        GradeBox.SelectedIndex = 0;
        ScenarioBox.ItemsSource = scenarios;
        ScenarioBox.DisplayMemberPath = "Name";
        ScenarioBox.SelectedIndex = -1;
        ScenarioTopicBox.ItemsSource = topics;
        ScenarioTopicBox.DisplayMemberPath = "Name";
        ScenarioTopicBox.SelectedIndex = 0;
        LoadCustomScenarios();
        RangeStyleBox.ItemsSource = new[] { "連続不等式で表示", "上下限を別々に表示" };
        RangeStyleBox.SelectedIndex = 0;
        GraphPresetBox.ItemsSource = new[] { "標準表示", "原点付近", "広い範囲", "三角関数 2π" };
        GraphPresetBox.SelectedIndex = 0;
        LessonTemplateBox.ItemsSource=new[]{"基本の探究","係数を比較","場面からモデル化"};LessonTemplateBox.SelectedIndex=0;
        FunctionList.ItemsSource = topics;
        FunctionList.DisplayMemberPath = "Name";
        FunctionList.SelectedIndex = 0;
        ComparisonList.ItemsSource = topics;
        ComparisonList.DisplayMemberPath = "Name";
        AccessibilityBox.ItemsSource = new[] { "標準", "高コントラスト", "色覚に配慮", "白黒印刷" };
        AccessibilityBox.SelectedIndex = 0;
        TextScaleBox.ItemsSource = new[] { "文字 100%", "文字 115%", "文字 130%" };
        TextScaleBox.SelectedIndex = 0;
        var colorNames=new[]{"朱", "青緑", "紫", "橙"};
        foreach(var box in new[]{ParamCompareAColor,ParamCompareBColor,ParamCompareCColor}){box.ItemsSource=colorNames;box.SelectedIndex=0;}
        ParamCompareAColor.SelectedIndex=0; ParamCompareBColor.SelectedIndex=1; ParamCompareCColor.SelectedIndex=2;
        Directory.CreateDirectory(DataDirectory);
        LoadRecentFiles();
        undoTimer.Tick+=(_,_)=>CommitUndoCheckpoint();
        autosaveTimer.Tick+=(_,_)=>WriteAutosave();
        replayTimer.Tick+=ReplayTimer_Tick;
        lessonTimer.Tick+=LessonTimer_Tick;
        initializing = false;
        LoadTopic();
        UpdateLessonStepIndicators();ResetLessonClock(false);
        lastTrackedState=CaptureLesson();
        AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler(TrackedTextChanged),true);
        AddHandler(ComboBox.SelectionChangedEvent,new SelectionChangedEventHandler(TrackedSelectionChanged),true);
        AddHandler(ListBox.SelectionChangedEvent,new SelectionChangedEventHandler(TrackedSelectionChanged),true);
        AddHandler(CheckBox.CheckedEvent,new RoutedEventHandler(TrackedCheckChanged),true);
        AddHandler(CheckBox.UncheckedEvent,new RoutedEventHandler(TrackedCheckChanged),true);
        autosaveTimer.Start();
        Loaded+=MainWindow_Loaded;
        Closed+=MainWindow_Closed;
    }

    private void GradeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || FunctionList is null) return;
        var grade = GradeBox.SelectedItem?.ToString() ?? grades[0];
        var filtered = grade == grades[0] ? topics : topics.Where(t => t.Grades.Contains(grade)).ToList();
        FunctionList.ItemsSource = filtered;
        FunctionList.SelectedIndex = 0;
        if (filtered.Count > 0) LoadTopic();
    }

    private void FunctionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initializing && FunctionList.SelectedItem is Topic)
        {
            if(!loadingScenario) { initializing=true; ScenarioBox.SelectedIndex=-1; ScenarioDescription.Text="場面を選ぶと、単位・問い・変域をセットします。"; initializing=false; }
            LoadTopic();
        }
    }

    private void ScenarioBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if(initializing || ScenarioBox.SelectedItem is not Scenario scene)return;
        var topic=topics.First(x=>x.Id==scene.TopicId);
        loadingScenario=true; initializing=true;
        GradeBox.SelectedItem=scene.Grade;
        FunctionList.ItemsSource=topics.Where(x=>x.Grades.Contains(scene.Grade)).ToList();
        FunctionList.SelectedItem=topic;
        ComparisonList.UnselectAll();
        initializing=false;
        LoadTopic();
        ParamASlider.Value=Math.Clamp(scene.A,ParamASlider.Minimum,ParamASlider.Maximum);
        ParamBSlider.Value=Math.Clamp(scene.B,ParamBSlider.Minimum,ParamBSlider.Maximum);
        ParamCSlider.Value=Math.Clamp(scene.C,ParamCSlider.Minimum,ParamCSlider.Maximum);
        RangeMinBox.Text=scene.Min; RangeMaxBox.Text=scene.Max;
        StartIncludedBox.IsChecked=true; EndIncludedBox.IsChecked=true;
        QuestionBox.Text=scene.Question; GoalBox.Text=scene.Goal; EvidenceBox.Text="値を変えて表・グラフを記録し、場面の数量関係を確かめよう。";
        ScenarioDescription.Text=scene.Description;
        loadingScenario=false;
        UpdateParameterLabels(); UpdateRangeDisplay(); DrawGraph();
    }

    private void ParamComparison_Changed(object sender, RoutedEventArgs e) { if(!initializing)DrawGraph(); }
    private void ParamComparison_Changed(object sender, TextChangedEventArgs e) { if(!initializing)DrawGraph(); }
    private void ParamComparison_Changed(object sender, SelectionChangedEventArgs e) { if(!initializing)DrawGraph(); }

    private List<(string Name, ParameterComparison Values)> EnabledParameterComparisons()
    {
        var boxes=new[]{(ParamCompareAEnabled,ParamCompareAValues,ParamCompareAName,ParamCompareAColor),(ParamCompareBEnabled,ParamCompareBValues,ParamCompareBName,ParamCompareBColor),(ParamCompareCEnabled,ParamCompareCValues,ParamCompareCName,ParamCompareCColor)};
        var result=new List<(string,ParameterComparison)>();
        var issues=new List<string>();
        foreach(var (toggle,box,nameBox,colorBox) in boxes)
        {
            if(toggle.IsChecked!=true)continue;
            var parts=box.Text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
            var name=string.IsNullOrWhiteSpace(nameBox.Text)?"比較":nameBox.Text.Trim();
            if(parts.Length!=3 || !double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var a)||!double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var b)||!double.TryParse(parts[2],NumberStyles.Float,CultureInfo.InvariantCulture,out var c)||new[]{a,b,c}.Any(x=>!double.IsFinite(x)||Math.Abs(x)>1e6))
            {issues.Add($"{name}：a, b, c/k を有限な数値3つ（カンマ区切り）で入力してください。");continue;}
            if((Current.Id is "exponential" or "logarithm") && a<=0){issues.Add($"{name}：底 a は0より大きい値にしてください。");continue;}
            if(Current.Id=="logarithm"&&Math.Abs(a-1)<1e-9){issues.Add($"{name}：対数の底 a は1にできません。");continue;}
            result.Add((name,new ParameterComparison{Enabled=true,A=a,B=b,C=c,Name=name,ColorIndex=Math.Clamp(colorBox.SelectedIndex,0,3)}));
        }
        ComparisonValidation.Text=string.Join(" ",issues);
        return result;
    }

    private List<ParameterComparison> CaptureParameterComparisons()
    {
        var result=new List<ParameterComparison>();
        var boxes=new[]{(ParamCompareAEnabled,ParamCompareAValues,ParamCompareAName,ParamCompareAColor),(ParamCompareBEnabled,ParamCompareBValues,ParamCompareBName,ParamCompareBColor),(ParamCompareCEnabled,ParamCompareCValues,ParamCompareCName,ParamCompareCColor)};
        foreach(var (toggle,box,nameBox,colorBox) in boxes)
        {
            var p=box.Text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
            double Read(int i)=>i<p.Length&&double.TryParse(p[i],NumberStyles.Float,CultureInfo.InvariantCulture,out var d)?d:0;
            result.Add(new ParameterComparison{Enabled=toggle.IsChecked==true,A=Read(0),B=Read(1),C=Read(2),Name=nameBox.Text,ColorIndex=Math.Clamp(colorBox.SelectedIndex,0,3)});
        }
        return result;
    }

    private void ApplyParameterComparisons(List<ParameterComparison> values)
    {
        var toggles=new[]{ParamCompareAEnabled,ParamCompareBEnabled,ParamCompareCEnabled};
        var boxes=new[]{ParamCompareAValues,ParamCompareBValues,ParamCompareCValues};
        var names=new[]{ParamCompareAName,ParamCompareBName,ParamCompareCName};
        var colors=new[]{ParamCompareAColor,ParamCompareBColor,ParamCompareCColor};
        for(int i=0;i<3;i++)
        {
            var p=i<values.Count?values[i]:new ParameterComparison();
            toggles[i].IsChecked=p.Enabled;
            boxes[i].Text=$"{p.A:0.##}, {p.B:0.##}, {p.C:0.##}";
            names[i].Text=string.IsNullOrWhiteSpace(p.Name)?$"比較 {(char)('A'+i)}":p.Name;
            colors[i].SelectedIndex=Math.Clamp(p.ColorIndex,0,3);
        }
    }

    private void Compare_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing) return;
        if(e.AddedItems.OfType<Topic>().Any(t=>FunctionList.SelectedItem is Topic current&&t.Id==current.Id))
        {
            initializing=true;
            foreach(var item in e.AddedItems.OfType<Topic>().Where(t=>FunctionList.SelectedItem is Topic current&&t.Id==current.Id).ToList())ComparisonList.SelectedItems.Remove(item);
            initializing=false;
        }
        if(ComparisonList.SelectedItems.Count>3 && e.AddedItems.Count>0)
        {
            initializing=true; ComparisonList.SelectedItems.Remove(e.AddedItems[0]); initializing=false;
            MessageBox.Show("比較対象は3つまで選べます。","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        DrawGraph();
    }

    private void DisplaySettings_Changed(object sender, SelectionChangedEventArgs e)
    {
        if(initializing || AccessibilityBox is null || TextScaleBox is null)return;
        FontSize=TextScaleBox.SelectedIndex switch{1=>14,2=>16,_=>12};
        DrawGraph();
    }

    private string[] CurrentPalette() => AccessibilityBox.SelectedIndex switch
    {
        1 => new[]{"#000000","#C00000","#005A9C","#6A1B9A"},
        2 => new[]{"#0072B2","#D55E00","#009E73","#CC79A7"},
        3 => new[]{"#222222","#666666","#999999","#444444"},
        _ => new[]{"#3559D8","#E05A47","#138A72","#8657B5"}
    };

    private string ComparisonColor(int index)=>AccessibilityBox.SelectedIndex switch
    {
        1=>new[]{"#C00000","#005A9C","#6A1B9A","#7A4A00"}[Math.Clamp(index,0,3)],
        2=>new[]{"#D55E00","#009E73","#CC79A7","#0072B2"}[Math.Clamp(index,0,3)],
        3=>new[]{"#222222","#666666","#999999","#444444"}[Math.Clamp(index,0,3)],
        _=>new[]{"#E05A47","#138A72","#8657B5","#D59B28"}[Math.Clamp(index,0,3)]
    };

    private void MainWindow_Loaded(object sender,RoutedEventArgs e)
    {
        var cleanMarker=Path.Combine(DataDirectory,"session.clean");
        var wasClean=File.Exists(cleanMarker);
        if(wasClean)File.Delete(cleanMarker);
        var autosave=Path.Combine(DataDirectory,"autosave.funlab.json");
        if(!wasClean&&File.Exists(autosave)&&new FileInfo(autosave).Length>0&&MessageBox.Show($"前回の終了時に保存されなかった作業が見つかりました。\n自動保存日時：{File.GetLastWriteTime(autosave):yyyy/MM/dd HH:mm}\n復元しますか？","関数探究ラボ",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)
        {
            try{LoadLessonFile(autosave,false,false);}catch(Exception ex){MessageBox.Show($"自動保存を読み込めませんでした。\n{ex.Message}","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
        lastTrackedState=CaptureLesson();
    }

    private void MainWindow_Closed(object? sender,EventArgs e)
    {
        autosaveTimer.Stop(); undoTimer.Stop(); replayTimer.Stop();lessonTimer.Stop(); WriteAutosave();
        File.WriteAllText(Path.Combine(DataDirectory,"session.clean"),DateTime.Now.ToString("O"));
    }

    private void WriteAutosave()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var path=Path.Combine(DataDirectory,"autosave.funlab.json");
            var temp=path+".tmp";
            File.WriteAllText(temp,JsonSerializer.Serialize(CaptureLesson()),new UTF8Encoding(false));
            File.Move(temp,path,true);
            SaveStatusText.Text=$"自動保存済み {DateTime.Now:HH:mm}（教材ファイルは未保存の場合があります）";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){SaveStatusText.Text="自動保存できません。保存先の空き容量・権限を確認してください。";}
    }

    private void LoadRecentFiles()
    {
        var path=Path.Combine(DataDirectory,"recent.json");
        var recent=new List<string>();
        try{if(File.Exists(path))recent=JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path))??new();}catch(JsonException){}
        recent=recent.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        initializing=true; RecentFilesBox.ItemsSource=recent; RecentFilesBox.SelectedIndex=-1; initializing=false;
    }

    private string CustomScenarioPath => Path.Combine(DataDirectory,"scenarios.json");
    private string FavoritesPath => Path.Combine(DataDirectory,"favorites.json");
    private HashSet<string> favorites = new(StringComparer.OrdinalIgnoreCase);
    private void LoadCustomScenarios()
    {
        try { if(File.Exists(CustomScenarioPath)) customScenarios.AddRange(JsonSerializer.Deserialize<List<Scenario>>(File.ReadAllText(CustomScenarioPath))??new()); } catch(JsonException) { }
        scenarios.AddRange(customScenarios); ScenarioBox.ItemsSource=null; ScenarioBox.ItemsSource=scenarios; ScenarioBox.DisplayMemberPath="Name";
        try { if(File.Exists(FavoritesPath)) favorites=new(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FavoritesPath))??new(),StringComparer.OrdinalIgnoreCase); } catch(JsonException) { }
    }

    private void LessonSearch_Changed(object sender,TextChangedEventArgs e)
    {
        if(initializing||RecentFilesBox is null)return;
        var all=new List<string>();try{if(File.Exists(Path.Combine(DataDirectory,"recent.json")))all=JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(DataDirectory,"recent.json")))??new();}catch(JsonException){}
        all=all.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();var q=LessonSearchBox.Text.Trim();
        if(q.Length>0)all=all.Where(p=>p.Contains(q,StringComparison.CurrentCultureIgnoreCase)||ReadLessonSearchText(p).Contains(q,StringComparison.CurrentCultureIgnoreCase)).ToList();
        all=all.Concat(favorites.Where(File.Exists).Where(p=>q.Length==0||p.Contains(q,StringComparison.CurrentCultureIgnoreCase)||ReadLessonSearchText(p).Contains(q,StringComparison.CurrentCultureIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        initializing=true;RecentFilesBox.ItemsSource=all;RecentFilesBox.SelectedIndex=-1;initializing=false;
    }
    private static string ReadLessonSearchText(string path){try{return File.ReadAllText(path);}catch{return "";}}
    private void FavoriteLesson_Click(object sender,RoutedEventArgs e)
    {
        if(string.IsNullOrWhiteSpace(currentLessonPath)){MessageBox.Show("お気に入り登録は、教材を保存した後に利用できます。","関数探究ラボ");return;}
        if(FavoriteLessonBox.IsChecked==true)favorites.Add(currentLessonPath);else favorites.Remove(currentLessonPath);
        File.WriteAllText(FavoritesPath,JsonSerializer.Serialize(favorites.ToList()),new UTF8Encoding(false));
    }
    private void StudentMode_Changed(object sender,RoutedEventArgs e)
    {
        if(StudentWorkspacePanel is null)return;
        var on=StudentModeBox.IsChecked==true;StudentWorkspacePanel.Visibility=on?Visibility.Visible:Visibility.Collapsed;TeacherPanel.Visibility=on?Visibility.Collapsed:Visibility.Visible;
        StudentQuestionDisplay.Text=QuestionBox.Text;StudentGoalDisplay.Text=GoalBox.Text;
    }
    private void LessonStep_Click(object sender,RoutedEventArgs e)
    {
        StudentModeBox.IsChecked=true;
        if(sender is Button b&&int.TryParse(b.Tag?.ToString(),out var step))SetLessonStep(step,true);
    }
    private void SetLessonStep(int step,bool log)
    {
        activeLessonStep=Math.Clamp(step,0,2);LessonFlowTabs.SelectedIndex=activeLessonStep;UpdateLessonStepIndicators();
        if(presentationMode){PresentationQuestionText.Text=QuestionBox.Text;PresentationGoalText.Text=GoalBox.Text;}
        if(log)LogActivity("授業ステップを選択",new[]{"問い・予想","操作・記録","説明・振り返り"}[activeLessonStep]);
    }
    private void UpdateLessonStepIndicators()
    {
        if(LessonProgressDisplay is null)return;
        var names=new[]{"問い・予想","操作・記録","説明・振り返り"};LessonProgressDisplay.Text=$"　{activeLessonStep+1}/3 {names[activeLessonStep]}";
        var buttons=new[]{LessonStep1Button,LessonStep2Button,LessonStep3Button};for(int i=0;i<buttons.Length;i++){buttons[i].Background=i==activeLessonStep?new SolidColorBrush(Color.FromRgb(35,122,112)):new SolidColorBrush(Color.FromRgb(234,243,239));buttons[i].Foreground=i==activeLessonStep?Brushes.White:new SolidColorBrush(Color.FromRgb(36,92,84));}
    }
    private void LessonFlowTabs_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(initializing||LessonFlowTabs is null)return;activeLessonStep=Math.Clamp(LessonFlowTabs.SelectedIndex,0,2);UpdateLessonStepIndicators();
    }
    private void PreviousLessonStep_Click(object sender,RoutedEventArgs e)=>SetLessonStep(activeLessonStep-1,true);
    private void NextLessonStep_Click(object sender,RoutedEventArgs e)=>SetLessonStep(activeLessonStep+1,true);
    private void LessonTimerToggle_Click(object sender,RoutedEventArgs e)
    {
        if(lessonTimer.IsEnabled){lessonTimer.Stop();return;}
        if(lessonRemaining<=TimeSpan.Zero)ResetLessonClock(true);
        lessonTimer.Start();LogActivity("授業タイマー開始",LessonMinutesBox.Text+"分");
    }
    private void LessonTimerReset_Click(object sender,RoutedEventArgs e)=>ResetLessonClock(true);
    private void ResetLessonClock(bool showError)
    {
        lessonTimer.Stop();if(!int.TryParse(LessonMinutesBox.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var minutes)||minutes<1||minutes>300){if(showError)MessageBox.Show("授業時間は1〜300分で入力してください。","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Warning);minutes=50;}
        lessonRemaining=TimeSpan.FromMinutes(minutes);LessonClockDisplay.Text=FormatClock(lessonRemaining);
    }
    private static string FormatClock(TimeSpan remaining)=>$"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
    private void LessonTimer_Tick(object? sender,EventArgs e)
    {
        if(lessonRemaining>TimeSpan.Zero)lessonRemaining-=TimeSpan.FromSeconds(1);if(lessonRemaining<TimeSpan.Zero)lessonRemaining=TimeSpan.Zero;LessonClockDisplay.Text=FormatClock(lessonRemaining);
        if(lessonRemaining==TimeSpan.Zero){lessonTimer.Stop();LessonClockDisplay.Text="時間終了";LogActivity("授業タイマー終了");}
    }
    private void ApplyLessonTemplate_Click(object sender,RoutedEventArgs e)
    {
        var kind=LessonTemplateBox.SelectedItem?.ToString()??"基本の探究";var t=Current;var scene=ScenarioBox.SelectedItem as Scenario;
        QuestionBox.Text=scene?.Question??$"{t.Name}で、変数を変えると関数の値やグラフはどう変化するだろう？";
        GoalBox.Text=scene?.Goal??$"{t.Name}の式・表・グラフを関連付け、変化の特徴を根拠とともに説明する。";
        PredictionBox.Text=$"操作する前に予想しよう：{t.Hint}";
        EvidenceBox.Text=kind switch{"係数を比較"=>"一度に1つの係数だけを変え、変える前後の式・表・グラフを比べて記録しよう。","場面からモデル化"=>$"場面：{scene?.Description??"身近な数量の場面を設定し、x と y が何を表すか決めよう。"}　変数・単位・変域を確かめて、式とグラフを対応させよう。",_=>"パラメータやxの値を変えて試し、式・表・グラフから根拠を2つ以上記録しよう。"};
        ExplainBox.Text="予想と結果を比べ、式・表・グラフの具体的な値を根拠にして規則を説明しよう。";
        SupportBox.Text="支援：x・y が表す量を確認し、変える値を一つに絞る。評価：予想、複数の表現からの根拠、数学的な説明を確認する。";
        StudentResponseBox.Text="予想と異なる結果が出たとき、表とグラフのどの値を確かめるか説明する。";
        if(!int.TryParse(LessonMinutesBox.Text,out var minutes)||minutes<1||minutes>300)LessonMinutesBox.Text="50";ResetLessonClock(false);
        if(kind=="係数を比較"){ParamCompareAEnabled.IsChecked=true;ParamCompareBEnabled.IsChecked=true;ParamCompareAValues.Text=$"{ParamASlider.Value+1:0.##}, {ParamBSlider.Value:0.##}, {ParamCSlider.Value:0.##}";ParamCompareBValues.Text=$"{ParamASlider.Value-1:0.##}, {ParamBSlider.Value:0.##}, {ParamCSlider.Value:0.##}";}
        SaveStatusText.Text="ひな型を適用しました・未保存";LogActivity("教材ひな型を適用",kind);
    }
    private void PresentationMode_Click(object sender,RoutedEventArgs e)
    {
        if(!presentationMode)
        {
            previousWindowState=WindowState;previousWindowStyle=WindowStyle;previousToolsPaneCollapsed=toolsPaneCollapsed;previousStudentMode=StudentModeBox.IsChecked==true;previousTableDockOpen=TableDockPanel.Visibility==Visibility.Visible;presentationMode=true;
            StudentModeBox.IsChecked=true;toolsPaneCollapsed=true;ToolsPane.Visibility=Visibility.Collapsed;ToolsColumn.Width=new GridLength(0);
            MainTitleBar.Visibility=Visibility.Collapsed;RibbonBar.Visibility=Visibility.Collapsed;
            GraphToolsBar.Visibility=Visibility.Collapsed;ProbeInputPanel.Visibility=Visibility.Collapsed;TableDockPanel.Visibility=Visibility.Collapsed;DockTableColumn.Width=new GridLength(0);ValueTablePopup.IsOpen=false;
            PresentationQuestionText.Text=QuestionBox.Text;PresentationGoalText.Text=GoalBox.Text;PresentationPromptPanel.Visibility=Visibility.Visible;
            WindowStyle=WindowStyle.None;WindowState=WindowState.Maximized;
            PresentationButton.Content="Esc で提示を終了";
        }
        else
        {
            presentationMode=false;WindowState=WindowState.Normal;WindowStyle=previousWindowStyle;WindowState=previousWindowState;
            MainTitleBar.Visibility=Visibility.Visible;RibbonBar.Visibility=Visibility.Visible;PresentationPromptPanel.Visibility=Visibility.Collapsed;
            GraphToolsBar.Visibility=Visibility.Visible;ProbeInputPanel.Visibility=Visibility.Visible;TableDockPanel.Visibility=previousTableDockOpen?Visibility.Visible:Visibility.Collapsed;DockTableColumn.Width=previousTableDockOpen?new GridLength(430):new GridLength(0);
            StudentModeBox.IsChecked=previousStudentMode;toolsPaneCollapsed=previousToolsPaneCollapsed;ToolsPane.Visibility=toolsPaneCollapsed?Visibility.Collapsed:Visibility.Visible;ToolsColumn.Width=toolsPaneCollapsed?new GridLength(0):new GridLength(320);ToolsToggleButton.Content=toolsPaneCollapsed?"設定を表示":"設定を隠す";
            PresentationButton.Content="提示全画面";
        }
    }
    private void GraphPreset_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(initializing||GraphPresetBox.SelectedItem is not string preset)return;
        initializing=true;
        var scene=ScenarioBox.SelectedItem as Scenario;
        var sceneMin=scene is not null&&TryParseBound(scene.Min,out var minValue)?minValue:0;
        switch(preset){case "原点付近":RangeMinBox.Text=scene?.Min??"-5";RangeMaxBox.Text=scene is null?"5":FormatBound(Math.Max(5,sceneMin+5));break;case "広い範囲":RangeMinBox.Text=scene?.Min??"-20";RangeMaxBox.Text=scene is null?"20":FormatBound(Math.Max(20,sceneMin+20));break;case "三角関数 2π":RangeMinBox.Text=scene?.Min??"-2π";RangeMaxBox.Text=scene?.Max??"2π";break;default:RangeMinBox.Text=scene?.Min??(Current.Id=="trigonometric"?"-2π":"-10");RangeMaxBox.Text=scene?.Max??(Current.Id=="trigonometric"?"2π":"10");break;}
        plotZoom=plotZoomY=1;plotPanX=plotPanY=0;initializing=false;UpdateRangeDisplay();UpdateParameterLabels();DrawGraph();LogActivity("グラフ表示範囲を変更",preset);TrackStateChange();
    }
    private void ToggleTableDock_Click(object sender,RoutedEventArgs e)
    {
        var open=TableDockPanel.Visibility!=Visibility.Visible;
        TableDockPanel.Visibility=open?Visibility.Visible:Visibility.Collapsed;DockTableColumn.Width=open?new GridLength(430):new GridLength(0);
        TableDockToggleButton.Content=open?"横並びを閉じる":"横並び表示";if(open)ValueTablePopup.IsOpen=false;DrawGraph();
    }
    private void ApplyProbeX_Click(object sender,RoutedEventArgs e)
    {
        if(!TryParseBound(ProbeAXBox.Text,out probeAx)||!TryParseBound(ProbeBXBox.Text,out probeBx)){MessageBox.Show("xA・xB は数値または π で入力してください。","関数探究ラボ");return;}
        if(Math.Abs(probeBx-probeAx)<1e-10){MessageBox.Show("平均変化率を求めるため、xA と xB は異なる値にしてください。","関数探究ラボ");return;}
        probeASet=probeBSet=true;UpdateProbeSummary();DrawGraph();LogActivity("座標を指定",$"xA={FormatBound(probeAx)}, xB={FormatBound(probeBx)}");TrackStateChange();
    }
    private void UpdateProbeSummary()
    {
        if(!probeASet){ProbeResultText.Text="";StudentProbeDisplay.Text="";return;}
        ProbeAXBox.Text=FormatBound(probeAx);probeAy=Current.Eval(probeAx,ParamASlider.Value,ParamBSlider.Value,ParamCSlider.Value);
        if(probeBSet){ProbeBXBox.Text=FormatBound(probeBx);probeBy=Current.Eval(probeBx,ParamASlider.Value,ParamBSlider.Value,ParamCSlider.Value);var slope=Math.Abs(probeBx-probeAx)<1e-10?double.NaN:(probeBy-probeAy)/(probeBx-probeAx);lastProbeText=$"A({FormatBound(probeAx)}, {FormatValue(probeAy)}) → B({FormatBound(probeBx)}, {FormatValue(probeBy)})　平均変化率 {FormatValue(slope)}";}
        else lastProbeText=$"A({FormatBound(probeAx)}, {FormatValue(probeAy)})";
        ProbeResultText.Text=lastProbeText;StudentProbeDisplay.Text=lastProbeText;
    }
    private void LogActivity(string action,string? detail=null)
    {
        if(replayingActivity)return;
        var item=new ActivityEvent(DateTime.Now,Current.Id,action,ParamASlider.Value,ParamBSlider.Value,ParamCSlider.Value,detail,probeASet,probeBSet,probeAx,probeBx,plotZoom,plotZoomY,plotPanX,plotPanY,RangeMinBox.Text,RangeMaxBox.Text);
        if(activityLog.Count>0&&activityLog[^1].Action==action&&(item.At-activityLog[^1].At).TotalMilliseconds<350)activityLog[^1]=item;else activityLog.Add(item);
        if(activityLog.Count>500)activityLog.RemoveAt(0);RefreshActivityTimeline();
    }
    private void RefreshActivityTimeline()=>ActivityTimelineBox.ItemsSource=activityLog.AsEnumerable().Reverse().Take(60).Select(x=>$"{x.At:HH:mm:ss}　{x.Action}　a={x.A:0.##}, b={x.B:0.##}, c/k={x.C:0.##}{(string.IsNullOrWhiteSpace(x.Detail)?"":"　"+x.Detail)}").ToList();
    private void ReplayActivity_Click(object sender,RoutedEventArgs e)
    {
        if(replayTimer.IsEnabled){replayTimer.Stop();replayingActivity=false;ReplayButton.Content="履歴を再開";return;}
        if(activityLog.Count==0)return;if(replayIndex>=activityLog.Count)replayIndex=0;replayingActivity=true;ReplayButton.Content="一時停止";replayTimer.Start();
    }
    private void ReplayTimer_Tick(object? sender,EventArgs e)
    {
        if(replayIndex>=activityLog.Count){replayTimer.Stop();replayingActivity=false;ReplayButton.Content="履歴を再生";return;}
        var item=activityLog[replayIndex++];
        if(Current.Id!=item.TopicId&&topics.FirstOrDefault(t=>t.Id==item.TopicId) is { } topic){initializing=true;var grade=topic.Grades.FirstOrDefault()??grades[0];GradeBox.SelectedItem=grade;FunctionList.ItemsSource=topics.Where(t=>t.Grades.Contains(grade)).ToList();FunctionList.SelectedItem=topic;initializing=false;LoadTopic();}
        initializing=true;ParamASlider.Value=Math.Clamp(item.A,ParamASlider.Minimum,ParamASlider.Maximum);ParamBSlider.Value=Math.Clamp(item.B,ParamBSlider.Minimum,ParamBSlider.Maximum);ParamCSlider.Value=Math.Clamp(item.C,ParamCSlider.Minimum,ParamCSlider.Maximum);RangeMinBox.Text=item.RangeMinimum;RangeMaxBox.Text=item.RangeMaximum;plotZoom=Math.Clamp(item.Zoom,0.25,30);plotZoomY=Math.Clamp(item.ZoomY,0.25,30);plotPanX=item.PanX;plotPanY=item.PanY;probeASet=item.HasProbeA;probeBSet=item.HasProbeB;probeAx=item.ProbeA;probeBx=item.ProbeB;initializing=false;
        UpdateRangeDisplay();UpdateParameterLabels();UpdateProbeSummary();DrawGraph();
    }
    private void ClearActivity_Click(object sender,RoutedEventArgs e){replayTimer.Stop();replayingActivity=false;replayIndex=0;ReplayButton.Content="履歴を再生";activityLog.Clear();RefreshActivityTimeline();}
    private void ShowReflectionCompare_Click(object sender,RoutedEventArgs e)
    {
        var prediction=string.IsNullOrWhiteSpace(StudentPredictionBox.Text)?"（予想はまだ記入されていません）":StudentPredictionBox.Text;
        var explanation=string.IsNullOrWhiteSpace(StudentExplanationBox.Text)?"（説明はまだ記入されていません）":StudentExplanationBox.Text;
        var observation=string.IsNullOrWhiteSpace(StudentObservationBox.Text)?"（観察記録はまだありません）":StudentObservationBox.Text;
        var grid=new Grid{Margin=new Thickness(14)};grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        TextBlock Header(string text)=>new(){Text=text,FontWeight=FontWeights.Bold,Margin=new Thickness(4)};
        TextBox Body(string text)=>new(){Text=text,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(4),Padding=new Thickness(8)};
        grid.Children.Add(Header("操作前の予想"));var h2=Header("結果をもとにした説明");Grid.SetColumn(h2,1);grid.Children.Add(h2);
        var p=Body(prediction);Grid.SetRow(p,1);grid.Children.Add(p);var x=Body(explanation);Grid.SetRow(x,1);Grid.SetColumn(x,1);grid.Children.Add(x);var o=Header("観察記録："+observation);o.TextWrapping=TextWrapping.Wrap;o.Margin=new Thickness(4,10,4,0);Grid.SetRow(o,2);Grid.SetColumnSpan(o,2);grid.Children.Add(o);
        var w=new Window{Title="予想と説明を比べる",Owner=this,Width=850,Height=520,MinWidth=600,MinHeight=350,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=grid};w.ShowDialog();
    }
    private void RecordStudentObservation_Click(object sender,RoutedEventArgs e)
    {
        var line=$"[{DateTime.Now:HH:mm}] {Current.Name}: a={ParamASlider.Value:0.##}, b={ParamBSlider.Value:0.##}, c/k={ParamCSlider.Value:0.##}; {RangeDisplay.Text}; {lastProbeText}";
        StudentObservationBox.AppendText((StudentObservationBox.Text.Length==0?"":"\n")+line);
        LogActivity("観察を記録",lastProbeText);
    }
    private void SaveStudentWork_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new SaveFileDialog{Title="生徒の探究記録を保存",Filter="探究記録 (*.funlab.student.json)|*.funlab.student.json|JSON (*.json)|*.json",FileName=$"{Current.Name}_探究記録.funlab.student.json",DefaultExt=".funlab.student.json",AddExtension=true};
        if(dialog.ShowDialog()!=true)return;
        var record=new { Grade=GradeBox.SelectedItem?.ToString(),Topic=Current.Name,Question=QuestionBox.Text,Goal=GoalBox.Text,Prediction=StudentPredictionBox.Text,Observations=StudentObservationBox.Text,Explanation=StudentExplanationBox.Text,Reflection=StudentReflectionBox.Text,SelfAssessment=new[]{RubricFormulaBox.IsChecked==true,RubricTableBox.IsChecked==true,RubricGraphBox.IsChecked==true,RubricReasonBox.IsChecked==true},ActivityLog=activityLog,SavedAt=DateTime.Now };
        File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(record,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(true));MessageBox.Show("探究記録を保存しました。","関数探究ラボ");
    }
    private void ReviewStudentSubmissions_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="生徒の探究記録をまとめて確認",Filter="生徒の探究記録 (*.funlab.student.json;*.json)|*.funlab.student.json;*.json|すべてのファイル (*.*)|*.*",Multiselect=true};
        if(dialog.ShowDialog()!=true)return;
        var submissions=new List<StudentSubmission>();var skipped=new List<string>();
        foreach(var path in dialog.FileNames)
        {
            try
            {
                var info=new FileInfo(path);if(info.Length>2_000_000)throw new InvalidDataException("ファイルが大きすぎます");
                using var doc=JsonDocument.Parse(File.ReadAllText(path));var root=doc.RootElement;
                string Text(string key)=>root.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
                if(!root.TryGetProperty("Topic",out _))throw new InvalidDataException("生徒記録の形式ではありません");
                var rubric=new bool[4];if(root.TryGetProperty("SelfAssessment",out var checks)&&checks.ValueKind==JsonValueKind.Array)for(int i=0;i<Math.Min(4,checks.GetArrayLength());i++)rubric[i]=checks[i].ValueKind==JsonValueKind.True;
                var saved=root.TryGetProperty("SavedAt",out var savedValue)&&savedValue.TryGetDateTime(out var date)?date:File.GetLastWriteTime(path);
                submissions.Add(new StudentSubmission(Text("Grade"),Text("Topic"),Text("Question"),Text("Prediction"),Text("Observations"),Text("Explanation"),Text("Reflection"),rubric,saved,Path.GetFileName(path),path));
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException){skipped.Add($"{Path.GetFileName(path)}：{ex.Message}");}
        }
        if(submissions.Count==0){MessageBox.Show("読み込める生徒記録がありませんでした。"+(skipped.Count>0?"\n"+string.Join("\n",skipped.Take(5)):""),"関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
        var table=new DataTable();foreach(var name in new[]{"ファイル","学年","単元","自己評価","保存日時","予想","観察","説明","振り返り"})table.Columns.Add(name);
        foreach(var item in submissions)table.Rows.Add(item.SourceFile,item.Grade,item.Topic,$"{item.SelfAssessment}/4",item.SavedAt.ToString("yyyy/MM/dd HH:mm"),item.Prediction,item.Observations,item.Explanation,item.Reflection);
        var grid=new DataGrid{ItemsSource=table.DefaultView,AutoGenerateColumns=true,IsReadOnly=true,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Single,MinHeight=250};
        var detail=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=160,Margin=new Thickness(0,8,0,0),Padding=new Thickness(8)};
        grid.SelectionChanged+=(_,_)=>{if(grid.SelectedItem is DataRowView row)detail.Text=$"予想：{row["予想"]}\n\n観察：{row["観察"]}\n\n説明：{row["説明"]}\n\n振り返り：{row["振り返り"]}";};
        var topicSummary=new DataGrid{ItemsSource=submissions.GroupBy(x=>new{x.Grade,x.Topic}).Select(g=>new{学年=g.Key.Grade,単元=g.Key.Topic,人数=g.Count(),自己評価平均=Math.Round(g.Average(x=>x.SelfAssessment),1),式=g.Count(x=>x.Rubric[0]),表=g.Count(x=>x.Rubric[1]),グラフ=g.Count(x=>x.Rubric[2]),説明=g.Count(x=>x.Rubric[3])}),AutoGenerateColumns=true,IsReadOnly=true,CanUserAddRows=false,MaxHeight=115,Margin=new Thickness(0,0,0,8)};
        var summary=new TextBlock{Text=$"読み込み {submissions.Count} 件　｜　自己評価平均 {submissions.Average(x=>x.SelfAssessment):0.0}/4　｜　基準別達成数を学年・単元ごとに集計"+(skipped.Count>0?$"　｜　読み込み対象外 {skipped.Count} 件":""),Foreground=new SolidColorBrush(Color.FromRgb(35,122,112)),FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)};
        var save=new Button{Content="一覧を CSV 保存",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0)};save.Click+=(_,_)=>
        {
            var target=new SaveFileDialog{Title="クラス記録をCSV保存",Filter="CSV (*.csv)|*.csv",FileName="探究記録_クラス一覧.csv",DefaultExt=".csv",AddExtension=true};if(target.ShowDialog()!=true)return;
            static string Q(string? s)=>"\""+(s??"").Replace("\"","\"\"").Replace("\r"," ").Replace("\n"," / ")+"\"";
            var lines=new List<string>{string.Join(",",new[]{"ファイル","学年","単元","自己評価数","式","表","グラフ","説明","保存日時","予想","観察","説明文","振り返り","教師コメント"}.Select(Q))};
            foreach(var item in submissions){var props=new[]{item.SourceFile,item.Grade,item.Topic,item.SelfAssessment.ToString(),item.Rubric[0]?"○":"",item.Rubric[1]?"○":"",item.Rubric[2]?"○":"",item.Rubric[3]?"○":"",item.SavedAt.ToString("yyyy/MM/dd HH:mm"),item.Prediction,item.Observations,item.Explanation,item.Reflection,ReadTeacherComment(item.SourcePath)};lines.Add(string.Join(",",props.Select(Q)));}
            try{File.WriteAllText(target.FileName,string.Join(Environment.NewLine,lines),new UTF8Encoding(true));MessageBox.Show("クラス記録をCSVに保存しました。","関数探究ラボ");}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){MessageBox.Show($"保存できませんでした。\n{ex.Message}","関数探究ラボ");}
        };
        var comment=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=58,Margin=new Thickness(0,5,0,0),Padding=new Thickness(8),ToolTip="選択中の生徒への教師コメント。生徒記録とは別ファイルに保存されます。"};var commentStatus=new TextBlock{Foreground=new SolidColorBrush(Color.FromRgb(35,122,112)),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0)};
        grid.SelectionChanged+=(_,_)=>{if(grid.SelectedItem is DataRowView row){detail.Text=$"予想：{row["予想"]}\n\n観察：{row["観察"]}\n\n説明：{row["説明"]}\n\n振り返り：{row["振り返り"]}";var selected=submissions.FirstOrDefault(x=>x.SourceFile==row["ファイル"].ToString());comment.Text=selected is null?"":ReadTeacherComment(selected.SourcePath);commentStatus.Text="";}};
        var saveComment=new Button{Content="教師コメントを保存",Padding=new Thickness(12,6,12,6)};saveComment.Click+=(_,_)=>{if(grid.SelectedItem is not DataRowView row){MessageBox.Show("先に一覧から生徒を選んでください。","関数探究ラボ");return;}var selected=submissions.First(x=>x.SourceFile==row["ファイル"].ToString());try{File.WriteAllText(selected.SourcePath+".teacher.json",JsonSerializer.Serialize(new{Comment=comment.Text,UpdatedAt=DateTime.Now},new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(true));commentStatus.Text=$"保存済み {DateTime.Now:HH:mm}";}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){MessageBox.Show($"コメントを保存できませんでした。\n{ex.Message}","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Error);}};
        var nextLesson=new Button{Content="集計から次時案を作成",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0)};nextLesson.Click+=(_,_)=>ShowNextLessonPlan(submissions);
        var panel=new DockPanel{Margin=new Thickness(12)};var footer=new StackPanel();footer.Children.Add(detail);footer.Children.Add(new TextBlock{Text="教師コメント（生徒の記録ファイルとは別に保存）",FontWeight=FontWeights.SemiBold,Margin=new Thickness(2,6,0,0)});footer.Children.Add(comment);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0)};buttons.Children.Add(saveComment);buttons.Children.Add(commentStatus);buttons.Children.Add(nextLesson);buttons.Children.Add(save);var close=new Button{Content="閉じる",Padding=new Thickness(12,6,12,6)};buttons.Children.Add(close);footer.Children.Add(buttons);DockPanel.SetDock(footer,Dock.Bottom);panel.Children.Add(footer);var top=new StackPanel();top.Children.Add(summary);top.Children.Add(topicSummary);DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);panel.Children.Add(grid);
        var window=new Window{Title="生徒の探究記録を確認",Owner=this,Width=1050,Height=720,MinWidth=760,MinHeight=520,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White,Content=panel};close.Click+=(_,_)=>window.Close();window.ShowDialog();
    }
    private static string ReadTeacherComment(string studentRecordPath)
    {
        try{var path=studentRecordPath+".teacher.json";if(!File.Exists(path))return "";using var doc=JsonDocument.Parse(File.ReadAllText(path));return doc.RootElement.TryGetProperty("Comment",out var comment)?comment.GetString()??"":"";}catch{return "";}
    }
    private void ShowNextLessonPlan(List<StudentSubmission> submissions)
    {
        var text=new TextBox{Text=BuildNextLessonPlan(submissions),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(12),FontSize=14};
        var save=new Button{Content="編集した次時案を保存",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,8,8,0)};
        var close=new Button{Content="閉じる",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,8,0,0)};
        var panel=new DockPanel{Margin=new Thickness(12)};var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};buttons.Children.Add(save);buttons.Children.Add(close);DockPanel.SetDock(buttons,Dock.Bottom);panel.Children.Add(buttons);panel.Children.Add(text);
        var window=new Window{Title="集計をもとにした次時の探究案",Owner=this,Width=760,Height=680,MinWidth=580,MinHeight=480,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White,Content=panel};
        save.Click+=(_,_)=>{var d=new SaveFileDialog{Title="次時の指導案を保存",Filter="Markdown (*.md)|*.md|テキスト (*.txt)|*.txt",FileName="次時の探究案.md",DefaultExt=".md",AddExtension=true};if(d.ShowDialog()!=true)return;try{File.WriteAllText(d.FileName,text.Text,new UTF8Encoding(true));MessageBox.Show("次時案を保存しました。","関数探究ラボ");}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){MessageBox.Show($"保存できませんでした。\n{ex.Message}","関数探究ラボ");}};
        close.Click+=(_,_)=>window.Close();window.ShowDialog();
    }
    private static string BuildNextLessonPlan(List<StudentSubmission> submissions)
    {
        var sb=new StringBuilder();sb.AppendLine("# 次時の探究案（クラス記録の集計から作成）").AppendLine().AppendLine($"作成日時：{DateTime.Now:yyyy/MM/dd HH:mm}").AppendLine("集計を手がかりにした案です。実際の記録や授業時間に合わせて編集してください。");
        foreach(var group in submissions.GroupBy(x=>new{x.Grade,x.Topic}).OrderBy(g=>g.Key.Grade).ThenBy(g=>g.Key.Topic))
        {
            var metrics=new[]{("式・係数",group.Count(x=>x.Rubric[0])),("表",group.Count(x=>x.Rubric[1])),("グラフ",group.Count(x=>x.Rubric[2])),("根拠を用いた説明",group.Count(x=>x.Rubric[3]))};
            var weakest=metrics.Min(x=>x.Item2);var focus=metrics.Where(x=>x.Item2==weakest).Select(x=>x.Item1).ToArray();
            sb.AppendLine().AppendLine($"## {group.Key.Grade}・{group.Key.Topic}").AppendLine($"対象記録：{group.Count()}件／自己評価平均：{group.Average(x=>x.SelfAssessment):0.0}/4").AppendLine("各観点のチェック数："+string.Join("、",metrics.Select(x=>$"{x.Item1} {x.Item2}/{group.Count()}"))).AppendLine($"重点候補：{string.Join("・",focus)}（チェック数が最少の観点）").AppendLine("### 次時の活動案");
            foreach(var area in focus)switch(area)
            {
                case "式・係数":sb.AppendLine("- 係数を1つずつ変え、式のどの値が変化を決めるか予想してからグラフと表で確認する。");break;
                case "表":sb.AppendLine("- x の値を複数選び、対応する y を表に記録する。差や倍率に注目し、グラフ上の変化と照合する。");break;
                case "グラフ":sb.AppendLine("- 変域を区切ってグラフを読み、切片・頂点・増減など単元に合う特徴を表の値と結び付ける。");break;
                case "根拠を用いた説明":sb.AppendLine("- 『予想』『表・グラフから見つけた数値』『その数値から言えること』の3文で説明を組み立て、ペアで根拠を確認する。");break;
            }
            sb.AppendLine("### 確認する問い").AppendLine("- どの値・形を根拠にしましたか？式・表・グラフの間で同じ特徴を説明できますか？").AppendLine("### 授業後のメモ").AppendLine("- ");
        }
        return sb.ToString();
    }
    private void AddCustomScenario_Click(object sender,RoutedEventArgs e)
    {
        if(ScenarioTopicBox.SelectedItem is not Topic topic||string.IsNullOrWhiteSpace(ScenarioNameBox.Text)||!double.TryParse(ScenarioMinBox.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var min)||!double.TryParse(ScenarioMaxBox.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var max)||min>=max){MessageBox.Show("関数・教材名・変域（下限 < 上限）を確認してください。","関数探究ラボ");return;}
        var s=new Scenario(ScenarioNameBox.Text.Trim(),topic.Id,GradeBox.SelectedItem?.ToString() is { } g&&g!=grades[0]?g:topic.Grades[0],ScenarioDescriptionBox.Text,ScenarioQuestionBox.Text,ScenarioGoalBox.Text,ParamASlider.Value,ParamBSlider.Value,ParamCSlider.Value,ScenarioMinBox.Text,ScenarioMaxBox.Text,ScenarioXUnitBox.Text,ScenarioYUnitBox.Text);
        customScenarios.RemoveAll(x=>x.Name==s.Name);customScenarios.Add(s);File.WriteAllText(CustomScenarioPath,JsonSerializer.Serialize(customScenarios,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
        scenarios.Add(s);ScenarioBox.ItemsSource=null;ScenarioBox.ItemsSource=scenarios;ScenarioBox.DisplayMemberPath="Name";ScenarioBox.SelectedItem=s;MessageBox.Show("場面教材を追加しました。次回起動後も利用できます。","関数探究ラボ");
    }
    private void ExportScenario_Click(object sender,RoutedEventArgs e)
    {
        var d=new SaveFileDialog{Title="自作場面をJSON出力",Filter="JSON (*.json)|*.json",FileName="自作場面教材.json"};if(d.ShowDialog()!=true)return;File.WriteAllText(d.FileName,JsonSerializer.Serialize(customScenarios,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(true));
    }
    private void ImportScenario_Click(object sender,RoutedEventArgs e)
    {
        var d=new OpenFileDialog{Title="自作場面をJSONから読み込む",Filter="JSON (*.json)|*.json"};if(d.ShowDialog()!=true)return;
        try{var items=JsonSerializer.Deserialize<List<Scenario>>(File.ReadAllText(d.FileName))??new();foreach(var s in items){customScenarios.RemoveAll(x=>x.Name==s.Name);customScenarios.Add(s);}File.WriteAllText(CustomScenarioPath,JsonSerializer.Serialize(customScenarios,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));scenarios.RemoveAll(x=>customScenarios.Any(c=>c.Name==x.Name));scenarios.AddRange(customScenarios);ScenarioBox.ItemsSource=null;ScenarioBox.ItemsSource=scenarios;ScenarioBox.DisplayMemberPath="Name";}catch(Exception ex){MessageBox.Show($"読み込めませんでした。\n{ex.Message}","関数探究ラボ");}
    }
    private void ExportPlot_Click(object sender,RoutedEventArgs e)
    {
        if(Plot.ActualWidth<1||Plot.ActualHeight<1)return;var d=new SaveFileDialog{Title="グラフをPNG保存",Filter="PNG画像 (*.png)|*.png",FileName=$"{Current.Name}_グラフ.png",DefaultExt=".png",AddExtension=true};if(d.ShowDialog()!=true)return;
        var bmp=new RenderTargetBitmap((int)Plot.ActualWidth,(int)Plot.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(Plot);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));using var fs=File.Create(d.FileName);enc.Save(fs);
    }
    private void ExportTableCsv_Click(object sender,RoutedEventArgs e)
    {
        var source=ValueTable.ItemsSource??FloatingValueTable.ItemsSource;if(source is not DataView view)return;var d=new SaveFileDialog{Title="表をCSV保存",Filter="CSV (*.csv)|*.csv",FileName=$"{Current.Name}_値の表.csv",DefaultExt=".csv",AddExtension=true};if(d.ShowDialog()!=true)return;
        string Q(object? v)=>"\""+(v?.ToString()?.Replace("\"","\"\"")??"")+"\"";var sb=new StringBuilder();sb.AppendLine(string.Join(",",view.Table!.Columns.Cast<DataColumn>().Select(c=>Q(c.ColumnName))));foreach(DataRowView row in view)sb.AppendLine(string.Join(",",row.Row!.ItemArray.Select(Q)));File.WriteAllText(d.FileName,sb.ToString(),new UTF8Encoding(true));
    }
    private void CurriculumMap_Click(object sender,RoutedEventArgs e)
    {
        var w=new Window{Title="学年・単元対応表",Owner=this,Width=780,Height=500,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White};
        var rows=new[]{("小6","算数・比例関係","比例関係の特徴を表・式・グラフで探究","小学校算数編・C 変化と関係"),("中1","比例・反比例","比例・反比例の式とグラフを比較","中学校数学編・関数"),("中2","一次関数","変化の割合・切片とグラフ","中学校数学編・一次関数"),("中3","関数 y=ax²","変域・変化の割合とグラフ","中学校数学編・関数 y=ax²"),("高 数学I","二次関数","平方完成・頂点・最大最小","高等学校数学編・数学I"),("高 数学II","指数・対数・三角関数","関数の特徴とグラフの相互比較","高等学校数学編・数学II")};
        var panel=new DockPanel();var note=new TextBlock{Text="対応は学習指導要領解説をもとにした目安です。教科書や学校の年間指導計画に合わせて調整してください。\n文部科学省：小学校算数編・中学校数学編・高等学校数学編",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(14),Foreground=Brushes.DimGray};DockPanel.SetDock(note,Dock.Bottom);panel.Children.Add(note);panel.Children.Add(new DataGrid{ItemsSource=rows.Select(r=>new{学年=r.Item1,単元=r.Item2,探究の焦点=r.Item3,対応箇所=r.Item4}),AutoGenerateColumns=true,IsReadOnly=true,Margin=new Thickness(12)});w.Content=panel;w.ShowDialog();
    }

    private void ToggleToolsPane_Click(object sender,RoutedEventArgs e)
    {
        toolsPaneCollapsed=!toolsPaneCollapsed;
        ToolsPane.Visibility=toolsPaneCollapsed?Visibility.Collapsed:Visibility.Visible;
        ToolsColumn.Width=toolsPaneCollapsed?new GridLength(0):new GridLength(320);
        ToolsToggleButton.Content=toolsPaneCollapsed?"設定を表示":"設定を隠す";
    }

    private void ToggleValueTable_Click(object sender,RoutedEventArgs e)
    {
        ValueTablePopup.IsOpen=!ValueTablePopup.IsOpen;
    }

    private void RememberRecentFile(string path)
    {
        var recent=RecentFilesBox.Items.Cast<string>().ToList();
        recent.RemoveAll(x=>string.Equals(x,path,StringComparison.OrdinalIgnoreCase));
        recent.Insert(0,path); recent=recent.Take(8).ToList();
        initializing=true; RecentFilesBox.ItemsSource=recent; RecentFilesBox.SelectedIndex=-1; initializing=false;
        try{File.WriteAllText(Path.Combine(DataDirectory,"recent.json"),JsonSerializer.Serialize(recent),new UTF8Encoding(false));}catch(IOException){}
    }

    private void RecentFiles_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(initializing||RecentFilesBox.SelectedItem is not string path||!File.Exists(path))return;
        try{LoadLessonFile(path,true,true);}catch(Exception ex){MessageBox.Show($"最近使った教材を開けませんでした。\n{ex.Message}","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Error);}
        initializing=true; RecentFilesBox.SelectedIndex=-1; initializing=false;
    }

    private void TrackedTextChanged(object sender,TextChangedEventArgs e)=>TrackStateChange();
    private void TrackedSelectionChanged(object sender,SelectionChangedEventArgs e)=>TrackStateChange();
    private void TrackedCheckChanged(object sender,RoutedEventArgs e)=>TrackStateChange();

    private void TrackStateChange()
    {
        if(initializing)return;
        var current=CaptureLesson();
        if(lastTrackedState is null){lastTrackedState=current;return;}
        if(JsonSerializer.Serialize(current)==JsonSerializer.Serialize(lastTrackedState))return;
        SaveStatusText.Text="未保存の変更があります";
        undoPendingState??=lastTrackedState;
        lastTrackedState=current;
        undoTimer.Stop();undoTimer.Start();
    }

    private void CommitUndoCheckpoint()
    {
        undoTimer.Stop();
        if(undoPendingState is not null)
        {
            undoStack.Push(undoPendingState);
            while(undoStack.Count>40){var keep=undoStack.Take(40).Reverse().ToArray();undoStack.Clear();foreach(var item in keep)undoStack.Push(item);}
        }
        undoPendingState=null;lastTrackedState=CaptureLesson();
    }

    private void Undo_Click(object sender,RoutedEventArgs e)
    {
        CommitUndoCheckpoint();
        if(undoStack.Count==0)return;
        var preset=undoStack.Pop(); ApplyLessonPreset(preset); lastTrackedState=CaptureLesson(); PlotCoordinates.Text="ひとつ前の操作に戻しました。";
    }

    private void MainWindow_PreviewKeyDown(object sender,System.Windows.Input.KeyEventArgs e)
    {
        if(presentationMode&&e.Key==System.Windows.Input.Key.Escape){PresentationMode_Click(PresentationButton,new RoutedEventArgs());e.Handled=true;return;}
        if(e.Key==System.Windows.Input.Key.Z&&(System.Windows.Input.Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Control)!=0){Undo_Click(this,new RoutedEventArgs());e.Handled=true;}
    }

    private void DuplicateLesson_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new SaveFileDialog{Title="教材の複製を保存",Filter="関数探究教材 (*.funlab.json)|*.funlab.json",FileName=$"{Current.Name}_探究教材_コピー.funlab.json",DefaultExt=".funlab.json",AddExtension=true};
        if(dialog.ShowDialog()!=true)return;
        try{File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(CaptureLesson(),new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(true));RememberRecentFile(dialog.FileName);currentLessonPath=dialog.FileName;SaveStatusText.Text=$"保存済み {DateTime.Now:HH:mm}";lastTrackedState=CaptureLesson();MessageBox.Show("教材の複製を保存しました。元の教材は変更されていません。","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Information);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){SaveStatusText.Text="保存に失敗しました";MessageBox.Show($"教材を保存できませんでした。\n{ex.Message}","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    private void ExportLessonPackage_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new SaveFileDialog{Title="教材パッケージを出力",Filter="関数探究パッケージ (*.funpack)|*.funpack",FileName=$"{Current.Name}_授業パッケージ.funpack",DefaultExt=".funpack",AddExtension=true};
        if(dialog.ShowDialog()!=true)return;
        using(var fs=File.Create(dialog.FileName))using(var archive=new ZipArchive(fs,ZipArchiveMode.Create))
        {
            WriteJson("manifest.json",new{Format="FunctionExplorerPackage",Version=1,CreatedAt=DateTime.Now});
            WriteJson("lesson.funlab.json",CaptureLesson());WriteJson("scenarios.json",customScenarios);
            void WriteJson<T>(string name,T value){var entry=archive.CreateEntry(name,CompressionLevel.Optimal);using var writer=new StreamWriter(entry.Open(),new UTF8Encoding(false));writer.Write(JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));}
        }
        MessageBox.Show("教材・場面ライブラリ・操作履歴をパッケージにまとめました。別のPCへ .funpack ファイルを渡してください。","関数探究ラボ");
    }

    private void ImportLessonPackage_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="教材パッケージを読み込む",Filter="関数探究パッケージ (*.funpack)|*.funpack|ZIPパッケージ (*.zip)|*.zip"};if(dialog.ShowDialog()!=true)return;
        try
        {
            using var archive=ZipFile.OpenRead(dialog.FileName);
            var manifestEntry=archive.GetEntry("manifest.json")??throw new InvalidDataException("パッケージ情報が見つかりません。");
            if(manifestEntry.Length>100_000)throw new InvalidDataException("パッケージ情報が大きすぎます。");
            using(var reader=new StreamReader(manifestEntry.Open(),Encoding.UTF8))using(var manifest=JsonDocument.Parse(reader.ReadToEnd()))
            {
                var root=manifest.RootElement;if(!root.TryGetProperty("Format",out var format)||format.GetString()!="FunctionExplorerPackage"||!root.TryGetProperty("Version",out var version)||version.GetInt32()!=1)throw new InvalidDataException("このパッケージ形式には対応していません。");
            }
            var lessonEntry=archive.GetEntry("lesson.funlab.json")??throw new InvalidDataException("lesson.funlab.json が見つかりません。");
            if(lessonEntry.Length>8_000_000)throw new InvalidDataException("教材データが大きすぎます。");
            LessonPreset preset;using(var reader=new StreamReader(lessonEntry.Open(),Encoding.UTF8))preset=JsonSerializer.Deserialize<LessonPreset>(reader.ReadToEnd())??throw new InvalidDataException("教材データが空です。");
            var scenarioEntry=archive.GetEntry("scenarios.json");
            List<Scenario> imported=new();if(scenarioEntry is not null){if(scenarioEntry.Length>8_000_000)throw new InvalidDataException("場面ライブラリが大きすぎます。");using var reader=new StreamReader(scenarioEntry.Open(),Encoding.UTF8);imported=JsonSerializer.Deserialize<List<Scenario>>(reader.ReadToEnd())??new();}
            if(imported.Count>500)throw new InvalidDataException("自作場面は一度に500件まで読み込めます。");
            if(!topics.Any(t=>t.Id==preset.TopicId))throw new InvalidDataException("この教材の関数単元には対応していません。");
            foreach(var item in imported)if(string.IsNullOrWhiteSpace(item.Name)||!topics.Any(t=>t.Id==item.TopicId)||!double.IsFinite(item.A)||!double.IsFinite(item.B)||!double.IsFinite(item.C)||!TryParseBound(item.Min,out var min)||!TryParseBound(item.Max,out var max)||min>=max)throw new InvalidDataException($"場面教材「{item.Name}」の内容を確認してください。");
            var conflicts=imported.Count(item=>customScenarios.Any(current=>string.Equals(current.Name,item.Name,StringComparison.OrdinalIgnoreCase)));
            var importedNames=string.Join("、",imported.Take(8).Select(x=>x.Name))+(imported.Count>8?$"、ほか {imported.Count-8} 件":"");
            var summary=$"教材：{preset.Grade}／{topics.First(t=>t.Id==preset.TopicId).Name}\n問い：{(string.IsNullOrWhiteSpace(preset.Question)?"（未設定）":preset.Question)}\n含まれる自作場面：{(imported.Count==0?"なし":importedNames)}\n同名の場面：{conflicts} 件\n操作履歴：{preset.ActivityLog?.Count??0} 件\n\n読み込みで現在の教材・画面設定が置き換わります。続行する前に未保存内容を保存してください。";
            var choice=new ComboBox{ItemsSource=new[]{"同名は別名で追加","同名は上書き","同名は読み込まない"},SelectedIndex=0,Margin=new Thickness(0,8,0,12)};
            var consent=new CheckBox{Content="内容を確認し、現在の作業を置き換えることに同意する",Margin=new Thickness(0,0,0,10)};
            var ok=new Button{Content="読み込む",IsEnabled=false,MinWidth=100,Padding=new Thickness(14,7,14,7)};var cancel=new Button{Content="キャンセル",MinWidth=100,Padding=new Thickness(14,7,14,7)};
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};buttons.Children.Add(ok);buttons.Children.Add(cancel);
            var panel=new DockPanel{Margin=new Thickness(18)};DockPanel.SetDock(buttons,Dock.Bottom);DockPanel.SetDock(cancel,Dock.Bottom);panel.Children.Add(buttons);var body=new StackPanel();body.Children.Add(new TextBlock{Text=summary,TextWrapping=TextWrapping.Wrap});body.Children.Add(new TextBlock{Text="同名の自作場面の扱い",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,14,0,0)});body.Children.Add(choice);body.Children.Add(consent);panel.Children.Add(body);
            var review=new Window{Title="教材パッケージの確認",Owner=this,Width=560,Height=390,MinWidth=500,MinHeight=350,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White,Content=panel};consent.Checked+=(_,_)=>ok.IsEnabled=true;consent.Unchecked+=(_,_)=>ok.IsEnabled=false;ok.Click+=(_,_)=>review.DialogResult=true;cancel.Click+=(_,_)=>review.DialogResult=false;
            if(review.ShowDialog()!=true)return;
            var policy=choice.SelectedIndex;foreach(var item in imported)
            {
                var existing=customScenarios.FirstOrDefault(x=>string.Equals(x.Name,item.Name,StringComparison.OrdinalIgnoreCase));
                if(existing is null){customScenarios.Add(item);continue;}
                if(policy==0){var name=item.Name;var suffix=2;while(customScenarios.Any(x=>string.Equals(x.Name,$"{name} ({suffix})",StringComparison.OrdinalIgnoreCase)))suffix++;customScenarios.Add(item with{Name=$"{name} ({suffix})"});}
                else if(policy==1){customScenarios.RemoveAll(x=>string.Equals(x.Name,item.Name,StringComparison.OrdinalIgnoreCase));customScenarios.Add(item);}
            }
            File.WriteAllText(CustomScenarioPath,JsonSerializer.Serialize(customScenarios,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));scenarios.RemoveAll(x=>customScenarios.Any(c=>string.Equals(c.Name,x.Name,StringComparison.OrdinalIgnoreCase)));scenarios.AddRange(customScenarios);ScenarioBox.ItemsSource=null;ScenarioBox.ItemsSource=scenarios;ScenarioBox.DisplayMemberPath="Name";
            if(preset.ScenarioName is not null&&!scenarios.Any(s=>string.Equals(s.Name,preset.ScenarioName,StringComparison.OrdinalIgnoreCase)))preset.ScenarioName=null;
            ApplyLessonPreset(preset);currentLessonPath=null;SaveStatusText.Text="パッケージ読込済み・未保存";MessageBox.Show("確認した教材パッケージを読み込みました。変更を残すには教材を保存してください。","関数探究ラボ");
        }
        catch(Exception ex){MessageBox.Show($"パッケージを読み込めませんでした。\n{ex.Message}","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    private void RecordObservation_Click(object sender, RoutedEventArgs e)
    {
        var note=string.IsNullOrWhiteSpace(ReflectionBox.Text)?"（気づきを追記してください）":ReflectionBox.Text.Trim();
        var row=$"[{DateTime.Now:HH:mm}] {Current.Name}　a={ParamASlider.Value:0.##}, b={ParamBSlider.Value:0.##}, c/k={ParamCSlider.Value:0.##}　変域 {RangeDisplay.Text}\n予想：{PredictionBox.Text}\n観察：{note}\n";
        JournalBox.AppendText(row+Environment.NewLine);
        JournalBox.ScrollToEnd();
        LogActivity("教師ログに記録",note);
    }

    private void SaveJournal_Click(object sender, RoutedEventArgs e)
    {
        var dialog=new SaveFileDialog{Title="探究記録を保存",Filter="探究記録 (*.funlab.log.txt)|*.funlab.log.txt|テキスト (*.txt)|*.txt",FileName=$"{Current.Name}_探究記録.funlab.log.txt"};
        if(dialog.ShowDialog()!=true)return;
        File.WriteAllText(dialog.FileName,BuildJournal(),new UTF8Encoding(true));
        MessageBox.Show("探究記録を保存しました。","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Information);
    }

    private void OpenJournal_Click(object sender, RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="探究記録を開く",Filter="探究記録 (*.funlab.log.txt;*.txt)|*.funlab.log.txt;*.txt|すべてのファイル (*.*)|*.*"};
        if(dialog.ShowDialog()!=true)return;
        JournalBox.Text=File.ReadAllText(dialog.FileName);
    }

    private string BuildJournal()=> $"関数探究ラボ　探究記録{Environment.NewLine}学年：{GradeBox.SelectedItem}{Environment.NewLine}単元：{Current.Name}{Environment.NewLine}予想：{PredictionBox.Text}{Environment.NewLine}{JournalBox.Text}{Environment.NewLine}操作履歴：{Environment.NewLine}{string.Join(Environment.NewLine,activityLog.Select(x=>$"{x.At:HH:mm:ss} {x.Action} a={x.A:0.##}, b={x.B:0.##}, c/k={x.C:0.##} {x.Detail}"))}";

    private void ValueTable_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        e.Column.IsReadOnly=e.PropertyName!="x";
        e.Column.Width=new DataGridLength(1,DataGridLengthUnitType.Star);
    }

    private void ValueTable_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if(e.Column.DisplayIndex!=0 || e.EditingElement is not TextBox box || e.Row.Item is not DataRowView editedRow)return;
        var editedX=box.Text;
        Dispatcher.BeginInvoke(new Action(()=>
        {
            var row=editedRow.Row; row[0]=editedX;
            if(!TryParseBound(editedX,out var x)){row.RowError="x には数値または π を入力してください";return;}
            row.ClearErrors();
            var index=row.Table.Rows.IndexOf(row);if(index>=0&&index<5){while(tableXValues.Count<5)tableXValues.Add(x);tableXValues[index]=x;}
            probeAx=x;probeASet=true;probeBSet=false;ProbeAXBox.Text=FormatBound(x);UpdateProbeSummary();DrawGraph();LogActivity("表のxを編集",$"xA={FormatBound(x)}");
            var t=Current; row[1]=FormatValue(t.Eval(x,ParamASlider.Value,ParamBSlider.Value,ParamCSlider.Value));
            var same=EnabledParameterComparisons();
            for(int i=0;i<same.Count;i++){var p=same[i].Values;row[i+2]=FormatValue(t.Eval(x,p.A,p.B,p.C));}
            var comps=SelectedComparisons.Take(3).ToList();
            for(int i=0;i<comps.Count;i++)row[i+2+same.Count]=FormatValue(comps[i].Eval(x,comps[i].DefaultA,comps[i].DefaultB,comps[i].DefaultC));
        }));
    }

    private void LoadTopic()
    {
        if (FunctionList.SelectedItem is not Topic t) return;
        plotZoom=plotZoomY=1;plotPanX=plotPanY=0;coordinatePinned=false;
        initializing = true;
        TopicTitle.Text = t.Name;
        RenderFormula(t.Formula);
        GradeTag.Text = t.Course;
        GraphPresetBox.SelectedIndex=0;
        ParamALabel.Text = t.A;
        ParamBLabel.Text = t.B;
        ParamCLabel.Text = t.C;
        ParamBPanel.Visibility = t.B == "—" ? Visibility.Collapsed : Visibility.Visible;
        ParamCPanel.Visibility = t.C == "—" ? Visibility.Collapsed : Visibility.Visible;

        ParamASlider.Minimum = t.Id is "exponential" or "logarithm" ? 0.25 : -5;
        ParamASlider.Maximum = 5;
        ParamASlider.TickFrequency = t.Id is "exponential" or "logarithm" ? 0.25 : 0.5;
        ParamBSlider.Minimum = t.Id == "trigonometric" ? 0.25 : -5;
        ParamBSlider.Maximum = t.Id == "trigonometric" ? 4 : 5;
        ParamBSlider.TickFrequency = t.Id == "trigonometric" ? 0.25 : 0.5;
        ParamCSlider.Minimum = -5;
        ParamCSlider.Maximum = 5;
        ParamASlider.Value = Math.Clamp(t.DefaultA, ParamASlider.Minimum, ParamASlider.Maximum);
        ParamBSlider.Value = Math.Clamp(t.DefaultB, ParamBSlider.Minimum, ParamBSlider.Maximum);
        ParamCSlider.Value = Math.Clamp(t.DefaultC, ParamCSlider.Minimum, ParamCSlider.Maximum);
        ParamCompareAEnabled.IsChecked=false; ParamCompareBEnabled.IsChecked=false; ParamCompareCEnabled.IsChecked=false;
        ParamCompareAValues.Text=$"{ParamASlider.Value+1:0.##}, {ParamBSlider.Value:0.##}, {ParamCSlider.Value:0.##}";
        var aBelow=ParamASlider.Value-1;
        if(t.Id=="logarithm"&&Math.Abs(aBelow-1)<1e-9)aBelow=0.5;
        ParamCompareBValues.Text=$"{aBelow:0.##}, {ParamBSlider.Value:0.##}, {ParamCSlider.Value:0.##}";
        ParamCompareCValues.Text=$"{ParamASlider.Value:0.##}, {ParamBSlider.Value+1:0.##}, {ParamCSlider.Value:0.##}";
        RangeMinBox.Text = t.Id == "trigonometric" ? "−2π" : "-10";
        RangeMaxBox.Text = t.Id == "trigonometric" ? "2π" : t.Id == "logarithm" ? "10" : "10";
        StartIncludedBox.IsChecked = t.Id != "logarithm";
        EndIncludedBox.IsChecked = true;
        ParamHint.Text = t.Hint;
        QuestionBox.Text = $"{t.Name}の式やグラフの特徴は、パラメータを変えるとどのように変化するだろう？";
        GoalBox.Text = $"{t.Name}について、式・表・グラフを関連付け、変化の規則を根拠とともに説明する。";
        PredictionBox.Text = "パラメータを変える前に、グラフや表がどう変化するか予想しよう。";
        EvidenceBox.Text = "値を変えて試し、式・表・グラフから根拠を記録しよう。";
        ExplainBox.Text = "見つけた規則を、予想と結果を比べながら説明しよう。";
        NotesBox.Text = "";
        LessonMinutesBox.Text = "50";
        StudentResponseBox.Text = "";
        SupportBox.Text = "";
        ReflectionBox.Text = "";
        initializing = false;
        UpdateParameterLabels();
        UpdateRangeDisplay();
        UpdateParameterLabels();
        UpdateProbeSummary();
        DrawGraph();
    }

    private void Parameter_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (initializing || ParamAValue is null || Plot is null) return;
        UpdateParameterLabels();
        DrawGraph();
        UpdateProbeSummary();
        LogActivity("係数を変更");
        TrackStateChange();
    }

    private void UpdateParameterLabels()
    {
        ParamAValue.Text = ParamASlider.Value.ToString("0.##", CultureInfo.InvariantCulture);
        ParamBValue.Text = ParamBSlider.Value.ToString("0.##", CultureInfo.InvariantCulture);
        ParamCValue.Text = ParamCSlider.Value.ToString("0.##", CultureInfo.InvariantCulture);
        if (Current.Id == "logarithm" && Math.Abs(ParamASlider.Value - 1) < 1e-9)
            ParamHint.Text = "底 a は 1 にできません。別の値を選ぶとグラフが表示されます。";
        else ParamHint.Text = Current.Hint;
        if(TryGetRange(out var min,out var max))
        {
            var left=Current.Eval(min,ParamASlider.Value,ParamBSlider.Value,ParamCSlider.Value);
            var right=Current.Eval(max,ParamASlider.Value,ParamBSlider.Value,ParamCSlider.Value);
            RateDisplay.Text=double.IsFinite(left)&&double.IsFinite(right)?$"この変域の平均変化率：(f({FormatBound(max)}) − f({FormatBound(min)})) ÷ ({FormatBound(max)} − {FormatBound(min)}) = {FormatValue((right-left)/(max-min))}":"この変域では平均変化率を計算できません。";
        }
        else RateDisplay.Text="有効な変域を入力すると平均変化率を表示します。";
        UpdateGraphFacts();
    }

    private void UpdateGraphFacts()
    {
        if(GraphFactsDisplay is null)return;
        var id=Current.Id;var a=ParamASlider.Value;var b=ParamBSlider.Value;var c=ParamCSlider.Value;
        var y0=id is "inverse" or "logarithm"?"定義されない":FormatValue(Current.Eval(0,a,b,c));
        string roots=id switch
        {
            "proportion" or "quadratic-jhs"=>Math.Abs(a)<1e-10?"すべてのx（x軸に一致）":"x = 0",
            "linear"=>Math.Abs(a)<1e-10?(Math.Abs(b)<1e-10?"すべてのx（x軸に一致）":"x軸と交わらない"):$"x = {FormatBound(-b/a)}",
            "quadratic-hs"=>QuadraticRoots(a,b,c),
            "logarithm"=>"x = 1",
            "exponential"=>"x軸と交わらない",
            "inverse"=>"x軸・y軸とは交わらない",
            "trigonometric"=>Math.Abs(b)<1e-10?"すべてのx（y=0）":"x = nπ / b（n は整数）",
            _=>"—"
        };
        var extra=id switch
        {
            "quadratic-hs"=>$"頂点：({FormatBound(b)}, {FormatValue(c)})　対称軸：x = {FormatBound(b)}",
            "quadratic-jhs"=>Math.Abs(a)<1e-10?"定数関数（a=0）":"頂点：(0, 0)　対称軸：y軸",
            "inverse"=>"漸近線：x=0、y=0",
            "trigonometric"=>Math.Abs(b)<1e-10?"周期：なし（定数関数）":$"振幅：{FormatValue(Math.Abs(a))}　周期：{FormatValue(2*Math.PI/Math.Abs(b))}",
            _=>""
        };
        GraphFactsDisplay.Text=$"y切片：{y0}　　x切片：{roots}"+(string.IsNullOrEmpty(extra)?"":$"\n{extra}");
        static string QuadraticRoots(double a,double h,double k)
        {
            if(Math.Abs(a)<1e-10)return Math.Abs(k)<1e-10?"すべてのx（x軸に一致）":"x軸と交わらない";
            var q=-k/a;if(q< -1e-10)return "実数解なし";if(Math.Abs(q)<1e-10)return $"x = {FormatBound(h)}（重解）";
            var d=Math.Sqrt(q);return $"x = {FormatBound(h-d)}, {FormatBound(h+d)}";
        }
    }

    private void Plot_SizeChanged(object sender, SizeChangedEventArgs e) => DrawGraph();

    private void Plot_MouseMove(object sender,System.Windows.Input.MouseEventArgs e)
    {
        if(Plot.ActualWidth<80||Plot.ActualHeight<80)return;
        var point=e.GetPosition(Plot);
        if(isPlotDragging)
        {
            var delta=point-dragStart;
            if(Math.Abs(point.X-plotDownPoint.X)>3||Math.Abs(point.Y-plotDownPoint.Y)>3)plotMoved=true;
            plotPanX-=delta.X/Math.Max(1,Plot.ActualWidth-76)*(graphXMax-graphXMin);
            plotPanY+=delta.Y/Math.Max(1,Plot.ActualHeight-76)*(graphYMax-graphYMin);
            dragStart=point; DrawGraph();
        }
        UpdatePlotCoordinates(point);
    }

    private void UpdatePlotCoordinates(Point point)
    {
        double x=graphXMin+(point.X-38)/Math.Max(1,Plot.ActualWidth-76)*(graphXMax-graphXMin);
        double y=graphYMax-(point.Y-38)/Math.Max(1,Plot.ActualHeight-76)*(graphYMax-graphYMin);
        var scene=ScenarioBox.SelectedItem as Scenario;
        PlotCoordinates.Text=$"x = {FormatBound(x)}{(scene is null?"":$" {scene.XUnit}")}　　y = {FormatValue(y)}{(scene is null?"":$" {scene.YUnit}")}";
    }

    private void Plot_MouseWheel(object sender,System.Windows.Input.MouseWheelEventArgs e)
    {
        var factor=e.Delta>0?1.2:1/1.2;
        plotZoom=Math.Clamp(plotZoom*factor,0.25,30); plotZoomY=Math.Clamp(plotZoomY*factor,0.25,30); DrawGraph();UpdatePlotCoordinates(e.GetPosition(Plot));LogActivity("グラフを拡大・縮小");TrackStateChange();e.Handled=true;
    }

    private void Plot_MouseLeftButtonDown(object sender,System.Windows.Input.MouseButtonEventArgs e)
    {
        Plot.Focus(); coordinatePinned=true; isPlotDragging=true; plotMoved=false;dragStart=e.GetPosition(Plot);plotDownPoint=dragStart; UpdatePlotCoordinates(dragStart); Plot.CaptureMouse();
    }

    private void Plot_MouseLeftButtonUp(object sender,System.Windows.Input.MouseButtonEventArgs e)
    {
        isPlotDragging=false; Plot.ReleaseMouseCapture();
        if(!plotMoved)
        {
            var x=graphXMin+(plotDownPoint.X-38)/Math.Max(1,Plot.ActualWidth-76)*(graphXMax-graphXMin);
            var y=graphYMax-(plotDownPoint.Y-38)/Math.Max(1,Plot.ActualHeight-76)*(graphYMax-graphYMin);
            if((System.Windows.Input.Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Shift)!=0){probeBx=x;probeBSet=true;}else{probeAx=x;probeASet=true;probeBSet=false;}
            if(!probeBSet)lastProbeText=$"点A：x={FormatBound(probeAx)}。次はShift+クリックで点Bを選びます。";
            ProbeAXBox.Text=FormatBound(probeAx);if(probeBSet)ProbeBXBox.Text=FormatBound(probeBx);UpdateProbeSummary();DrawGraph();LogActivity("グラフ上で点を選択",probeBSet?$"xA={FormatBound(probeAx)}, xB={FormatBound(probeBx)}":$"xA={FormatBound(probeAx)}");
        }
        else LogActivity("グラフを移動");
        TrackStateChange();
    }

    private void Plot_MouseLeave(object sender,System.Windows.Input.MouseEventArgs e)
    {
        if(!isPlotDragging&&!coordinatePinned)PlotCoordinates.Text="グラフ上をポイント／クリックで座標を表示　・　ホイールで拡大　・　ドラッグで移動";
    }

    private void ResetPlotView_Click(object sender,RoutedEventArgs e)
    {
        plotZoom=plotZoomY=1;plotPanX=plotPanY=0;coordinatePinned=false;DrawGraph();PlotCoordinates.Text="グラフ上をポイント／クリックで座標を表示　・　ホイールで拡大　・　ドラッグで移動";LogActivity("グラフ表示をリセット");
    }

    private void Range_ValueChanged(object sender, TextChangedEventArgs e)
    {
        if (initializing || RangeDisplay is null) return;
        UpdateRangeDisplay();
        UpdateParameterLabels();
        DrawGraph();
    }

    private void RangeOption_Changed(object sender, RoutedEventArgs e)
    {
        if (initializing || RangeDisplay is null) return;
        UpdateRangeDisplay();
        DrawGraph();
    }

    private void RangeStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || RangeDisplay is null) return;
        UpdateRangeDisplay();
        DrawGraph();
    }

    private void RenderFormula(string formula)
    {
        Formula.Inlines.Clear();
        AppendFormulaInlines(Formula.Inlines, formula, Formula.FontSize);
    }

    private static void AppendFormulaInlines(InlineCollection inlines, string formula, double fontSize)
    {
        for (int i = 0; i < formula.Length;)
        {
            if ((formula[i] == '^' || formula[i] == '_') && i + 1 < formula.Length)
            {
                var script = formula[i] == '^' ? BaselineAlignment.Superscript : BaselineAlignment.Subscript;
                int start = ++i;
                while (i < formula.Length && (char.IsLetterOrDigit(formula[i]) || formula[i] is '−' or '-')) i++;
                if (start == i) { AddRun(formula[start - 1].ToString(), false); continue; }
                var run = new Run(formula[start..i]) { FontSize = fontSize * 0.68, BaselineAlignment = script };
                inlines.Add(run);
                continue;
            }
            if (char.IsLetter(formula[i]))
            {
                int start = i++;
                while (i < formula.Length && char.IsLetter(formula[i])) i++;
                var token = formula[start..i];
                AddRun(token, token is not ("sin" or "log"));
                continue;
            }
            AddRun(formula[i].ToString(), false);
            i++;
        }
        void AddRun(string text, bool italic) => inlines.Add(new Run(text) { FontStyle = italic ? FontStyles.Italic : FontStyles.Normal, FontSize = fontSize });
    }

    private bool TryGetRange(out double min, out double max)
    {
        var validMin = TryParseBound(RangeMinBox.Text, out min);
        var validMax = TryParseBound(RangeMaxBox.Text, out max);
        if (!validMin || !validMax || min >= max)
        {
            RangeError.Text = "下限・上限には数値または π を入力し、下限を上限より小さくしてください。";
            return false;
        }
        if(ScenarioBox.SelectedItem is Scenario scene&&TryParseBound(scene.Min,out var requiredMin)&&min<requiredMin)
        {
            RangeError.Text=$"この場面では x は {scene.Min} 以上です。現実の条件に合う変域を指定してください。";
            return false;
        }
        RangeError.Text = "";
        return true;
    }

    private void UpdateRangeDisplay()
    {
        if (!TryGetRange(out var min, out var max))
        {
            RangeDisplay.Text = "変域を確認してください";
            return;
        }
        var lower = FormatBound(min);
        var upper = FormatBound(max);
        var includeMin = StartIncludedBox.IsChecked == true;
        var includeMax = EndIncludedBox.IsChecked == true;
        if (RangeStyleBox.SelectedIndex == 1)
        {
            var lowerSymbol = includeMin ? "≧" : "＞";
            var upperSymbol = includeMax ? "≦" : "＜";
            RangeDisplay.Text = $"x {lowerSymbol} {lower}、x {upperSymbol} {upper}";
        }
        else
        {
            var lowerSymbol = includeMin ? "≦" : "＜";
            var upperSymbol = includeMax ? "≦" : "＜";
            RangeDisplay.Text = $"{lower} {lowerSymbol} x {upperSymbol} {upper}";
        }
    }

    private static bool TryParseBound(string text, out double value)
    {
        var s = text.Trim().Replace('−', '-').Replace(" ", "");
        if (s.EndsWith('π'))
        {
            var coefficient = s[..^1];
            if (coefficient is "" or "+") value = Math.PI;
            else if (coefficient == "-") value = -Math.PI;
            else if (double.TryParse(coefficient, NumberStyles.Float, CultureInfo.InvariantCulture, out var c)) value = c * Math.PI;
            else { value = 0; return false; }
            return double.IsFinite(value);
        }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
               double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static string FormatBound(double value)
    {
        var multiple = value / Math.PI;
        var half = Math.Round(multiple * 2);
        if (Math.Abs(multiple - half / 2) < 1e-8)
        {
            if (half == 0) return "0";
            var sign = half < 0 ? "−" : "";
            var abs = Math.Abs((int)half);
            if(abs%2==0){var whole=abs/2;return sign+(whole==1?"π":whole.ToString(CultureInfo.InvariantCulture)+"π");}
            return sign+(abs==1?"π/2":abs.ToString(CultureInfo.InvariantCulture)+"π/2");
        }
        return value.ToString("0.##", CultureInfo.InvariantCulture).Replace('-', '−');
    }

    private void DrawGraph()
    {
        if (Plot is null || Plot.ActualWidth < 20 || Plot.ActualHeight < 20) return;
        Plot.Children.Clear();
        if(FunctionList.SelectedItem is not Topic t||!TryGetRange(out var domainMin,out var domainMax)){ValueTable.ItemsSource=null;FloatingValueTable.ItemsSource=null;return;}
        double w = Plot.ActualWidth, h = Plot.ActualHeight, pad = 38;
        double domainSpan = domainMax - domainMin;
        double viewSpan=domainSpan/plotZoom;
        double viewCenter=(domainMin+domainMax)/2+plotPanX;
        double xMin=viewCenter-viewSpan/2-viewSpan*0.08;
        double xMax=viewCenter+viewSpan/2+viewSpan*0.08;
        graphXMin=xMin; graphXMax=xMax;
        var parameterComparisons=EnabledParameterComparisons();
        var crossComparisons=SelectedComparisons.Take(3).ToList();
        var graphFunctions=new List<(Topic Topic,double A,double B,double C)>{(t,ParamASlider.Value,ParamBSlider.Value,ParamCSlider.Value)};
        graphFunctions.AddRange(parameterComparisons.Select(x=>(t,x.Values.A,x.Values.B,x.Values.C)));
        graphFunctions.AddRange(crossComparisons.Select(x=>(x,x.DefaultA,x.DefaultB,x.DefaultC)));
        var sampledValues=new List<double>();
        var visibleMin=Math.Max(domainMin,xMin); var visibleMax=Math.Min(domainMax,xMax);
        for(int i=0;i<=400&&visibleMax>visibleMin;i++)
        {
            var x=visibleMin+(visibleMax-visibleMin)*i/400;
            foreach(var fn in graphFunctions){var y=fn.Topic.Eval(x,fn.A,fn.B,fn.C);if(double.IsFinite(y)&&Math.Abs(y)<1e9)sampledValues.Add(y);}
        }
        double rawYMin=sampledValues.Count==0?-10:Math.Min(0,sampledValues.Min());
        double rawYMax=sampledValues.Count==0?10:Math.Max(0,sampledValues.Max());
        double yPadding=Math.Max((rawYMax-rawYMin)*0.1,1);
        double autoMin=rawYMin-yPadding,autoMax=rawYMax+yPadding;
        double yCenter=(autoMin+autoMax)/2+plotPanY;
        double yHalf=(autoMax-autoMin)/(2*plotZoomY);
        double yMin=yCenter-yHalf,yMax=yCenter+yHalf;
        graphYMin=yMin;graphYMax=yMax;
        double X(double x) => pad + (x - xMin) / (xMax - xMin) * (w - 2 * pad);
        double Y(double y) => h - pad - (y - yMin) / (yMax - yMin) * (h - 2 * pad);

        var gridColor=AccessibilityBox.SelectedIndex==3?"#D0D0D0":"#E5EAE5";
        var axisColor=AccessibilityBox.SelectedIndex==3?"#333333":"#879895";
        if (t.Id == "trigonometric")
        {
            int first = (int)Math.Ceiling(xMin / (Math.PI / 2));
            int last = (int)Math.Floor(xMax / (Math.PI / 2));
            for (int i = first; i <= last; i++)
            {
                var x = i * Math.PI / 2;
                AddLine(X(x), pad, X(x), h-pad, i==0 ? axisColor : gridColor, i==0 ? 1.6 : 1);
                AddText(FormatBound(x), X(x)-17, Y(0)+5);
            }
        }
        else
        {
            var step = NiceStep((xMax-xMin)/8);
            var first = Math.Ceiling(xMin/step)*step;
            for (var x=first; x<=xMax+step*1e-8; x+=step)
            {
                AddLine(X(x),pad,X(x),h-pad,Math.Abs(x)<step*1e-8?axisColor:gridColor,Math.Abs(x)<step*1e-8?1.6:1);
                if(Math.Abs(x)>step*1e-8) AddText(FormatBound(x),X(x)-12,Y(0)+5);
            }
        }
        var yStep=NiceStep((yMax-yMin)/7);
        var firstY=Math.Ceiling(yMin/yStep)*yStep;
        for(var y=firstY;y<=yMax+yStep*1e-8;y+=yStep)
        {
            var isZero=Math.Abs(y)<yStep*1e-8;
            AddLine(pad,Y(y),w-pad,Y(y),isZero?axisColor:gridColor,isZero?1.6:1);
            if(!isZero)AddText(y.ToString("0.##",CultureInfo.InvariantCulture),xMin<=0&&xMax>=0?X(0)+4:pad+3,Y(y)-9);
        }
        var sceneForAxes=ScenarioBox.SelectedItem as Scenario;
        AddText(sceneForAxes is null?"x":$"x ({sceneForAxes.XUnit})",w-pad+8,Y(0)-10);
        AddText(sceneForAxes is null?"y":$"y ({sceneForAxes.YUnit})",xMin<=0&&xMax>=0?X(0)+8:pad+8,pad-18);

        DrawCurve(t, ParamASlider.Value, ParamBSlider.Value, ParamCSlider.Value, CurrentPalette()[0], false, presentationMode?4:3,0);
        DrawEndpoint(domainMin, StartIncludedBox.IsChecked==true);
        DrawEndpoint(domainMax, EndIncludedBox.IsChecked==true);
        var comparisons = crossComparisons;
        for(int i=0;i<parameterComparisons.Count;i++)
        {
            var comparison=parameterComparisons[i]; var color=ComparisonColor(comparison.Values.ColorIndex);
            DrawCurve(t,comparison.Values.A,comparison.Values.B,comparison.Values.C,color,true,2.3,i);
            AddLegend($"- {comparison.Name}",color,100+i*95);
        }
        for (int i=0; i<comparisons.Count; i++)
        {
            var other = comparisons[i];
            var colors = CurrentPalette();
            var position=100+(parameterComparisons.Count+i)*95;
            DrawCurve(other, other.DefaultA, other.DefaultB, other.DefaultC, colors[(i+parameterComparisons.Count+1)%colors.Length], true, 2.2,i+parameterComparisons.Count);
            AddLegend($"┄ {other.Name}", colors[(i+parameterComparisons.Count+1)%colors.Length], position);
        }
        if(comparisons.Count+parameterComparisons.Count>0) AddLegend("● 現在", CurrentPalette()[0], 10);
        if(probeASet)
        {
            probeAy=t.Eval(probeAx,ParamASlider.Value,ParamBSlider.Value,ParamCSlider.Value);
            if(double.IsFinite(probeAy)&&probeAx>=xMin&&probeAx<=xMax&&probeAy>=yMin&&probeAy<=yMax)AddProbe(X(probeAx),Y(probeAy),"A","#D55E00");
        }
        if(probeBSet)
        {
            probeBy=t.Eval(probeBx,ParamASlider.Value,ParamBSlider.Value,ParamCSlider.Value);
            if(double.IsFinite(probeBy)&&probeBx>=xMin&&probeBx<=xMax&&probeBy>=yMin&&probeBy<=yMax)AddProbe(X(probeBx),Y(probeBy),"B","#0072B2");
            if(probeASet&&double.IsFinite(probeAy)&&double.IsFinite(probeBy))AddLine(X(probeAx),Y(probeAy),X(probeBx),Y(probeBy),"#D59B28",1.8);
        }
        UpdateTable(t);

        void AddLine(double x1,double y1,double x2,double y2,string color,double thickness)
            => Plot.Children.Add(new Line{X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=Brush(color),StrokeThickness=thickness});
        void AddText(string value,double x,double y)
            => Plot.Children.Add(new TextBlock{Text=value,FontSize=10,Foreground=Brush("#69758C"),RenderTransform=new TranslateTransform(x,y)});
        void AddLegend(string text,string color,double x)
            => Plot.Children.Add(new TextBlock{Text=text,FontSize=10,FontWeight=FontWeights.SemiBold,Foreground=Brush(color),Background=Brush("#FFFFFF"),RenderTransform=new TranslateTransform(x,4)});
        void AddProbe(double x,double y,string label,string color){Plot.Children.Add(new Ellipse{Width=12,Height=12,Fill=Brush(color),Stroke=Brush("#FFFFFF"),StrokeThickness=2,ToolTip=label,RenderTransform=new TranslateTransform(x-6,y-6)});AddText(label,x+7,y-8);}
        Brush Brush(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
        void DrawCurve(Topic topic,double a,double b,double c,string color,bool dashed,double thickness,int pattern)
        {
            double curveStart=Math.Max(domainMin,xMin),curveEnd=Math.Min(domainMax,xMax);
            if(curveEnd<=curveStart)return;
            var runs = new List<List<Point>>();
            var points = new List<Point>();
            int steps = Math.Max(350, (int)(w * 1.4));
            for(int i=0;i<=steps;i++)
            {
                double x=curveStart+(curveEnd-curveStart)*i/steps;
                if((i==0 && Math.Abs(x-domainMin)<1e-8&&StartIncludedBox.IsChecked!=true)||(i==steps && Math.Abs(x-domainMax)<1e-8&&EndIncludedBox.IsChecked!=true)) continue;
                double y=topic.Eval(x,a,b,c);
                if(!double.IsFinite(y)||y<yMin-1||y>yMax+1)
                {
                    if(points.Count>1)runs.Add(points);
                    points=new List<Point>();
                    continue;
                }
                points.Add(new Point(X(x),Y(y)));
            }
            if(points.Count>1)runs.Add(points);
            foreach(var run in runs)
            {
                var line=new Polyline{Stroke=Brush(color),StrokeThickness=thickness,StrokeLineJoin=PenLineJoin.Round};
                if(dashed) line.StrokeDashArray=(pattern%3) switch{0=>new DoubleCollection{5,4},1=>new DoubleCollection{2,3},_=>new DoubleCollection{8,3,2,3}};
                foreach(var point in run)line.Points.Add(point);
                Plot.Children.Add(line);
            }
        }
        void DrawEndpoint(double x,bool included)
        {
            double y=t.Eval(x,ParamASlider.Value,ParamBSlider.Value,ParamCSlider.Value);
            if(!double.IsFinite(y)||y<yMin||y>yMax)return;
            var dot=new Ellipse{Width=10,Height=10,Stroke=new SolidColorBrush(Color.FromRgb(53,89,216)),StrokeThickness=2,Fill=included?new SolidColorBrush(Color.FromRgb(53,89,216)):Brush("#FFFFFF")};
            Canvas.SetLeft(dot,X(x)-5); Canvas.SetTop(dot,Y(y)-5); Plot.Children.Add(dot);
        }
    }

    private static double NiceStep(double raw)
    {
        if(!double.IsFinite(raw)||raw<=0)return 1;
        var power=Math.Pow(10,Math.Floor(Math.Log10(raw)));
        var fraction=raw/power;
        return (fraction<=1?1:fraction<=2?2:fraction<=5?5:10)*power;
    }

    private void UpdateTable(Topic t)
    {
        if(!TryGetRange(out var min,out var max))return;
        var table = new DataTable();
        table.Columns.Add("x", typeof(string));
        table.Columns.Add($"y = {t.Formula.Split('=')[0].Trim()}", typeof(string));
        var parameterComparisons=EnabledParameterComparisons();
        var comparisons = SelectedComparisons.Take(3).ToList();
        foreach(var compare in parameterComparisons)table.Columns.Add($"比較 {compare.Name}",typeof(string));
        foreach(var compare in comparisons) table.Columns.Add($"基準：{compare.Name}", typeof(string));
        bool includeMin=StartIncludedBox.IsChecked==true, includeMax=EndIncludedBox.IsChecked==true;
        int gaps=4+(includeMin?0:1)+(includeMax?0:1);
        if(tableXValues.Count!=5)tableXValues=Enumerable.Range(0,5).Select(i=>min+(max-min)*(i+(includeMin?0:1))/gaps).ToList();
        for(int i=0;i<5;i++)
        {
            double x=tableXValues[i];
            double y=t.Eval(x,ParamASlider.Value,ParamBSlider.Value,ParamCSlider.Value);
            var row = table.NewRow();
            row[0] = FormatBound(x);
            row[1] = FormatValue(y);
            for(int j=0;j<parameterComparisons.Count;j++){var p=parameterComparisons[j].Values;row[j+2]=FormatValue(t.Eval(x,p.A,p.B,p.C));}
            for(int j=0;j<comparisons.Count;j++){var compare=comparisons[j];row[j+2+parameterComparisons.Count]=FormatValue(compare.Eval(x,compare.DefaultA,compare.DefaultB,compare.DefaultC));}
            table.Rows.Add(row);
        }
        ValueTable.ItemsSource = table.DefaultView;
        FloatingValueTable.ItemsSource = table.DefaultView;
    }

    private void SaveLesson_Click(object sender, RoutedEventArgs e)
    {
        var t=Current;
        var dialog=new SaveFileDialog{Title="教材ファイルを保存",Filter="関数探究教材 (*.funlab.json)|*.funlab.json|JSON ファイル (*.json)|*.json",FileName=$"{t.Name}_探究教材.funlab.json",DefaultExt=".funlab.json",AddExtension=true};
        if(dialog.ShowDialog()!=true)return;
        if(!TryGetRange(out _,out _)){MessageBox.Show("変域の入力を確認してください。修正してから保存できます。","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
        if(!int.TryParse(LessonMinutesBox.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var minutes)||minutes<1||minutes>300){MessageBox.Show("授業時間は1〜300分で入力してください。","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Warning);LessonMinutesBox.Focus();return;}
        try{var preset=CaptureLesson();File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(preset,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(true));RememberRecentFile(dialog.FileName);currentLessonPath=dialog.FileName;SaveStatusText.Text=$"保存済み {DateTime.Now:HH:mm}";lastTrackedState=CaptureLesson();MessageBox.Show("教材を保存しました。後から開いて編集できます。","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Information);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){SaveStatusText.Text="保存に失敗しました";MessageBox.Show($"教材を保存できませんでした。\n{ex.Message}","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    private LessonPreset CaptureLesson() => new()
    {
        TopicId=Current.Id, Grade=GradeBox.SelectedItem?.ToString() ?? grades[0],
        A=ParamASlider.Value, B=ParamBSlider.Value, C=ParamCSlider.Value,
        ComparisonTopicIds=SelectedComparisons.Select(x=>x.Id).ToArray(),
        Question=QuestionBox.Text, Goal=GoalBox.Text, Prediction=PredictionBox.Text,
        Evidence=EvidenceBox.Text, Explanation=ExplainBox.Text, Notes=NotesBox.Text,
        RangeMinimum=RangeMinBox.Text, RangeMaximum=RangeMaxBox.Text,
        IncludeMinimum=StartIncludedBox.IsChecked==true, IncludeMaximum=EndIncludedBox.IsChecked==true,
        RangeStyle=RangeStyleBox.SelectedIndex, LessonMinutes=LessonMinutesBox.Text,
        StudentResponses=StudentResponseBox.Text, SupportAndAssessment=SupportBox.Text,
        Reflection=ReflectionBox.Text, JournalContents=JournalBox.Text,
        ParameterComparisons=CaptureParameterComparisons(), ScenarioName=(ScenarioBox.SelectedItem as Scenario)?.Name,
        Tags=LessonTagsBox.Text,Favorite=currentLessonPath is not null&&favorites.Contains(currentLessonPath),
        StudentPrediction=StudentPredictionBox.Text,StudentObservation=StudentObservationBox.Text,StudentExplanation=StudentExplanationBox.Text,StudentReflection=StudentReflectionBox.Text,
        Rubric=new[]{RubricFormulaBox.IsChecked==true,RubricTableBox.IsChecked==true,RubricGraphBox.IsChecked==true,RubricReasonBox.IsChecked==true},
        ActivityLog=activityLog.ToList(),
        TableXValues=tableXValues.ToArray(),
        ViewZoom=plotZoom,ViewZoomY=plotZoomY,ViewPanX=plotPanX,ViewPanY=plotPanY
    };

    private void OpenLesson_Click(object sender, RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="保存した教材を開く",Filter="関数探究教材 (*.funlab.json;*.json)|*.funlab.json;*.json|すべてのファイル (*.*)|*.*"};
        if(dialog.ShowDialog()!=true)return;
        try
        {
            LoadLessonFile(dialog.FileName,true,true);
        }
        catch(Exception ex){initializing=false;MessageBox.Show($"教材を開けませんでした。\n{ex.Message}","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    private void LoadLessonFile(string path,bool remember,bool showMessage)
    {
        var preset=JsonSerializer.Deserialize<LessonPreset>(File.ReadAllText(path));
        if(preset is null)throw new InvalidDataException("教材ファイルを読み取れませんでした。");
        ApplyLessonPreset(preset);currentLessonPath=path;FavoriteLessonBox.IsChecked=favorites.Contains(path);
        if(remember)RememberRecentFile(path);
        SaveStatusText.Text=$"読み込み済み {DateTime.Now:HH:mm}";
        TrackStateChange();
        if(showMessage)MessageBox.Show("教材を開きました。","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Information);
    }

    private void ApplyLessonPreset(LessonPreset preset)
    {
        var topic=topics.FirstOrDefault(x=>x.Id==preset.TopicId) ?? throw new InvalidDataException("この教材に対応する関数が見つかりません。");
        initializing=true;
        GradeBox.SelectedItem=grades.Contains(preset.Grade)?preset.Grade:grades[0];
        FunctionList.ItemsSource=GradeBox.SelectedItem?.ToString()==grades[0]?topics:topics.Where(x=>x.Grades.Contains(GradeBox.SelectedItem?.ToString() ?? "")).ToList();
        FunctionList.SelectedItem=topic;
        if(FunctionList.SelectedItem is null){GradeBox.SelectedItem=grades[0];FunctionList.ItemsSource=topics;FunctionList.SelectedItem=topic;}
        initializing=false; LoadTopic(); initializing=true;
        ParamASlider.Value=Math.Clamp(preset.A,ParamASlider.Minimum,ParamASlider.Maximum);
        ParamBSlider.Value=Math.Clamp(preset.B,ParamBSlider.Minimum,ParamBSlider.Maximum);
        ParamCSlider.Value=Math.Clamp(preset.C,ParamCSlider.Minimum,ParamCSlider.Maximum);
        plotZoom=Math.Clamp(preset.ViewZoom,0.25,30);plotZoomY=Math.Clamp(preset.ViewZoomY,0.25,30);plotPanX=preset.ViewPanX;plotPanY=preset.ViewPanY;
        RangeMinBox.Text=preset.RangeMinimum; RangeMaxBox.Text=preset.RangeMaximum;
        StartIncludedBox.IsChecked=preset.IncludeMinimum; EndIncludedBox.IsChecked=preset.IncludeMaximum;
        RangeStyleBox.SelectedIndex=Math.Clamp(preset.RangeStyle,0,1);
        ApplyParameterComparisons(preset.ParameterComparisons??new());
        var savedScenario=scenarios.FirstOrDefault(x=>x.Name==preset.ScenarioName);
        ScenarioBox.SelectedItem=savedScenario;
        ScenarioDescription.Text=savedScenario?.Description??"場面を選ぶと、単位・問い・変域をセットします。";
        LessonTagsBox.Text=preset.Tags??"";StudentPredictionBox.Text=preset.StudentPrediction??"";StudentObservationBox.Text=preset.StudentObservation??"";StudentExplanationBox.Text=preset.StudentExplanation??"";StudentReflectionBox.Text=preset.StudentReflection??"";
        var rubric=preset.Rubric??Array.Empty<bool>();RubricFormulaBox.IsChecked=rubric.ElementAtOrDefault(0);RubricTableBox.IsChecked=rubric.ElementAtOrDefault(1);RubricGraphBox.IsChecked=rubric.ElementAtOrDefault(2);RubricReasonBox.IsChecked=rubric.ElementAtOrDefault(3);
        StudentQuestionDisplay.Text=preset.Question;StudentGoalDisplay.Text=preset.Goal;
        activityLog.Clear();activityLog.AddRange(preset.ActivityLog??new());RefreshActivityTimeline();
        tableXValues=(preset.TableXValues??Array.Empty<double>()).Where(double.IsFinite).Take(5).ToList();
        ComparisonList.UnselectAll();
        var oldIds=preset.ComparisonTopicIds?.Length>0?preset.ComparisonTopicIds:(preset.CompareTopicId is null?Array.Empty<string>():new[]{preset.CompareTopicId});
        foreach(var topicId in oldIds.Take(3)){var item=topics.FirstOrDefault(x=>x.Id==topicId);if(item is not null)ComparisonList.SelectedItems.Add(item);}
        QuestionBox.Text=preset.Question; GoalBox.Text=preset.Goal; PredictionBox.Text=preset.Prediction;
        EvidenceBox.Text=preset.Evidence; ExplainBox.Text=preset.Explanation; NotesBox.Text=preset.Notes;
        LessonMinutesBox.Text=preset.LessonMinutes; StudentResponseBox.Text=preset.StudentResponses; SupportBox.Text=preset.SupportAndAssessment;
        ReflectionBox.Text=preset.Reflection; JournalBox.Text=preset.JournalContents;
        initializing=false; UpdateParameterLabels(); UpdateRangeDisplay(); DrawGraph();
    }

    private void ExportStudent_Click(object sender, RoutedEventArgs e) => ExportWorksheetText(false);
    private void ExportTeacher_Click(object sender, RoutedEventArgs e) => ExportWorksheetText(true);

    private void ExportWorksheetText(bool teacher)
    {
        var suffix=teacher?"教師用":"生徒用";
        var dialog=new SaveFileDialog{Title=$"{suffix}教材をテキスト出力",Filter="Markdown (*.md)|*.md|テキスト (*.txt)|*.txt",FileName=$"{Current.Name}_{suffix}_ワークシート.md",DefaultExt=".md",AddExtension=true};
        if(dialog.ShowDialog()!=true)return;
        File.WriteAllText(dialog.FileName,BuildWorksheetText(includeTeacherNotes:teacher),new UTF8Encoding(true));
        MessageBox.Show("教材を出力しました。","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Information);
    }

    private void PrintStudent_Click(object sender, RoutedEventArgs e) => PrintWorksheet(false);
    private void PrintTeacher_Click(object sender, RoutedEventArgs e) => PrintWorksheet(true);

    private void PrintWorksheet(bool teacher)
    {
        try
        {
            var doc=BuildPrintDocument(teacher);
            var preview=new Window{Title=teacher?"教師用ワークシート プレビュー":"生徒用ワークシート プレビュー",Owner=this,Width=950,Height=820,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(244,242,237))};
            var layout=new Grid{Margin=new Thickness(12)};layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
            var toolbar=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
            var printButton=new Button{Content="印刷／PDF に保存",Padding=new Thickness(16,8,16,8)};
            printButton.Click+=(_,_)=>{var dialog=new PrintDialog();if(dialog.ShowDialog()==true)dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator,teacher?"関数探究ワークシート・教師用":"関数探究ワークシート・生徒用");};
            toolbar.Children.Add(printButton);var closeButton=new Button{Content="閉じる"};closeButton.Click+=(_,_)=>preview.Close();toolbar.Children.Add(closeButton);layout.Children.Add(toolbar);
            var viewer=new DocumentViewer{Document=doc,Margin=new Thickness(0,10,0,0)};Grid.SetRow(viewer,1);layout.Children.Add(viewer);preview.Content=layout;preview.ShowDialog();
        }
        catch(Exception ex){MessageBox.Show($"印刷を開始できませんでした。\n{ex.Message}","関数探究ラボ",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    private FlowDocument BuildPrintDocument(bool includeTeacherNotes)
    {
        var t=Current;
        var doc=new FlowDocument{PageWidth=793.7,PageHeight=1122.5,PagePadding=new Thickness(52),ColumnWidth=689,FontFamily=new FontFamily("Yu Gothic UI"),FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(30,41,59))};
        doc.Blocks.Add(new Paragraph(new Run($"{t.Name}　探究ワークシート")){FontSize=22,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,10)});
        var details=new Paragraph{Foreground=new SolidColorBrush(Color.FromRgb(80,92,112)),Margin=new Thickness(0,0,0,5)};
        details.Inlines.Add(new Run($"対象：{t.Course}　　　関数："));
        AppendFormulaInlines(details.Inlines,t.Formula,11);
        doc.Blocks.Add(details);
        doc.Blocks.Add(new Paragraph(new Run($"変域：{RangeDisplay.Text}")){Foreground=new SolidColorBrush(Color.FromRgb(35,122,112)),FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
        if(ScenarioBox.SelectedItem is Scenario scenario)AddSection("探究場面",scenario.Description);
        if(Plot.ActualWidth>0 && Plot.ActualHeight>0)
        {
            var chart=new RenderTargetBitmap((int)Plot.ActualWidth,(int)Plot.ActualHeight,96,96,PixelFormats.Pbgra32);
            chart.Render(Plot);
            doc.Blocks.Add(new BlockUIContainer(new Image{Source=chart,Width=470,Height=270,Stretch=Stretch.Uniform}){Margin=new Thickness(0,4,0,10)});
        }
        AddSection("学習のねらい",GoalBox.Text);
        AddSection("探究の問い",QuestionBox.Text);
        AddSection("1. 予想する",PredictionBox.Text+"\n\n予想：____________________________________________________________");
        AddSection("2. 試して記録する",EvidenceBox.Text+"\n\nパラメータの値：________________　結果：____________________________\n\n式・表・グラフから分かったこと：____________________________________");
        AddSection("3. 説明・まとめ",ExplainBox.Text+"\n\n見つけた規則を、式・表・グラフを根拠に説明しよう。\n\n__________________________________________________________________\n\n__________________________________________________________________");
        if(includeTeacherNotes)
        {
            AddSection("教師用：授業設計",$"授業時間：{LessonMinutesBox.Text} 分\n想定する生徒の反応：{StudentResponseBox.Text}\nつまずきへの支援・評価の観点：{SupportBox.Text}\n授業メモ：{NotesBox.Text}");
            AddSection("比較条件",BuildComparisonSummary());
        }
        var footer=new Paragraph(new Run($"操作値：a = {ParamASlider.Value:0.##}　b = {ParamBSlider.Value:0.##}　c / k = {ParamCSlider.Value:0.##}")){FontSize=9,Foreground=new SolidColorBrush(Color.FromRgb(100,110,128)),Margin=new Thickness(0,14,0,0)};
        doc.Blocks.Add(footer);
        return doc;
        void AddSection(string heading,string body)
        {
            doc.Blocks.Add(new Paragraph(new Run(heading)){FontSize=14,FontWeight=FontWeights.Bold,Margin=new Thickness(0,12,0,5)});
            doc.Blocks.Add(new Paragraph(new Run(body)){LineHeight=21,Margin=new Thickness(0,0,0,4)});
        }
    }

    private string BuildWorksheetText(bool includeTeacherNotes)
    {
        var t=Current; var sb=new StringBuilder();
        sb.AppendLine($"# {t.Name} 探究ワークシート").AppendLine().AppendLine($"対象：{t.Course}").AppendLine($"関数：{PlainFormula(t.Formula)}").AppendLine($"変域：{RangeDisplay.Text}").AppendLine();
        if(ScenarioBox.SelectedItem is Scenario scenario)sb.AppendLine("## 探究場面").AppendLine(scenario.Description).AppendLine();
        sb
          .AppendLine("## 学習のねらい").AppendLine(GoalBox.Text).AppendLine().AppendLine("## 探究の問い").AppendLine(QuestionBox.Text).AppendLine()
          .AppendLine("## 1. 予想する").AppendLine(PredictionBox.Text).AppendLine("予想：________________________________________________").AppendLine()
          .AppendLine("## 2. 試して記録する").AppendLine(EvidenceBox.Text).AppendLine("パラメータの値：____________　結果：____________").AppendLine()
          .AppendLine("## 3. 説明・まとめ").AppendLine(ExplainBox.Text).AppendLine("式・表・グラフを根拠に説明しよう。\n\n記入欄：________________________________________________").AppendLine();
        if(includeTeacherNotes) sb.AppendLine("## 授業設計（教師用）").AppendLine($"授業時間：{LessonMinutesBox.Text} 分").AppendLine($"想定する生徒の反応：{StudentResponseBox.Text}").AppendLine($"つまずきへの支援・評価の観点：{SupportBox.Text}").AppendLine().AppendLine("## 比較条件").AppendLine(BuildComparisonSummary()).AppendLine().AppendLine("## 教師用メモ").AppendLine(NotesBox.Text);
        return sb.ToString();
    }

    private string BuildComparisonSummary()
    {
        var lines=new List<string>{$"現在：a={ParamASlider.Value:0.##}, b={ParamBSlider.Value:0.##}, c/k={ParamCSlider.Value:0.##}"};
        lines.AddRange(EnabledParameterComparisons().Select(x=>$"同じ関数・{x.Values.Name}（{ComparisonColorName(x.Values.ColorIndex)}）：a={x.Values.A:0.##}, b={x.Values.B:0.##}, c/k={x.Values.C:0.##}"));
        lines.AddRange(SelectedComparisons.Select(x=>$"別関数：{x.Name}（初期値）"));
        return string.Join(Environment.NewLine,lines);
    }

    private static string ComparisonColorName(int index)=>new[]{"朱","青緑","紫","橙"}[Math.Clamp(index,0,3)];

    private static string PlainFormula(string formula) => formula;
    private static string FormatValue(double value) => double.IsFinite(value)?value.ToString("0.##",CultureInfo.InvariantCulture):"定義されない";
}


using EasyCon.Capture;
using EasyCon.Capture.Ocr.Frlg;
using EasyCon.Core;
using EzCv;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

if (args.Contains("--list-scenes"))
{
    Console.WriteLine(JsonSerializer.Serialize(FrlgScenes.All, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
if (args.Length < 2 || args.Contains("--help"))
{
    Console.WriteLine("FrlgOcrReplay IMAGE SCENE [--roi x,y,w,h] [--expected TEXT] [--output DIR] [--repeat 100]\nUse --list-scenes for supported keys. Name targets: FRLG_JPN_NAME:ミニリュウ|ハクリュー");
    return args.Contains("--help") ? 0 : 2;
}
try
{
    string imagePath = Path.GetFullPath(args[0]);
    string scene = args[1];
    string? expected = Option("--expected");
    string? output = Option("--output");
    int repeat = int.Parse(Option("--repeat") ?? "1", CultureInfo.InvariantCulture);
    if (repeat < 1 || repeat > 10000)
        throw new ArgumentOutOfRangeException(nameof(repeat), "Repeat must be 1..10000.");
    using Mat frame = Mat.FromImageData(File.ReadAllBytes(imagePath));
    Rect roi = FrlgOcr.DefaultRegion(scene, frame.Width, frame.Height);
    if (Option("--roi") is string region)
    {
        int[] values = region.Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        if (values.Length != 4) throw new ArgumentException("ROI must be x,y,w,h.");
        roi = new Rect(values[0], values[1], values[2], values[3]);
    }
    using OcrEngineCache cache = new();
    EasyScript.OcrDelegate recognize = OcrDelegateFactory.Create((Func<Mat>)(() => frame.Clone()), cache);
    List<double> durations = [];
    Dictionary<string, int> counts = [];
    long startingMemory = Process.GetCurrentProcess().PrivateMemorySize64;
    for (int i = 0; i < repeat; i++)
    {
        Stopwatch timer = Stopwatch.StartNew();
        string text = recognize(roi.X, roi.Y, roi.Width, roi.Height, scene);
        durations.Add(timer.Elapsed.TotalMilliseconds);
        counts[text] = counts.GetValueOrDefault(text) + 1;
    }
    FrlgReadResult result = cache.LastFrlgResult ?? throw new InvalidOperationException("Scene was not dispatched.");
    if (output != null)
    {
        Directory.CreateDirectory(output);
        FrlgOcr.ReadFrame(frame, roi, scene, output, cache.FrlgText);
    }
    double[] warm = durations.Skip(1).Order().ToArray();
    object report = new
    {
        version = FrlgOcr.Version,
        image = imagePath,
        scene,
        roi = new { roi.X, roi.Y, roi.Width, roi.Height },
        expected,
        repeat,
        counts,
        firstMilliseconds = durations[0],
        warmMedianMilliseconds = warm.Length == 0 ? (double?)null : warm[warm.Length / 2],
        privateMemoryGrowthBytes = Process.GetCurrentProcess().PrivateMemorySize64 - startingMemory,
        result
    };
    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
    Console.WriteLine(json);
    if (output != null)
        File.WriteAllText(Path.Combine(output, "replay.json"), json);
    return result.Success && (expected == null || counts.Count == 1 && counts.ContainsKey(expected)) ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

string? Option(string key)
{
    int index = Array.IndexOf(args, key);
    if (index < 0) return null;
    if (index + 1 >= args.Length) throw new ArgumentException($"Missing value for {key}.");
    return args[index + 1];
}
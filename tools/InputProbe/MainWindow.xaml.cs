using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace InputProbe;

public partial class MainWindow : Window
{
    private readonly Stopwatch _stopwatch = new();
    private readonly List<KeyEvent> _events = new();

    // VK name lookup for readable logging (letters/digits map to their char; OEM keys named).
    private static readonly Dictionary<int, string> VkNames = new()
    {
        [0x08] = "BACK", [0x09] = "TAB", [0x0D] = "ENTER", [0x10] = "SHIFT",
        [0x11] = "CTRL", [0x12] = "ALT", [0x20] = "SPACE",
        [0xBA] = "OEM1(;)", [0xBB] = "OEM_PLUS(=)", [0xBC] = "OEM_COMMA(,)",
        [0xBD] = "OEM_MINUS(-)", [0xBE] = "OEM_PERIOD(.)", [0xBF] = "OEM_2(/)",
        [0xC0] = "OEM_3(`)", [0xDB] = "OEM_4([)", [0xDC] = "OEM_5(\\)",
        [0xDD] = "OEM_6(])", [0xDE] = "OEM_7(')",
    };

    public MainWindow()
    {
        InitializeComponent();
        CaptureStatus.Text = "Ready — focus this window and start typing (or have TypeGent type here).";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_stopwatch.IsRunning)
            _stopwatch.Restart();

        var vk = KeyInterop.VirtualKeyFromKey(e.Key);
        RecordEvent(vk, isDown: true);
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        var vk = KeyInterop.VirtualKeyFromKey(e.Key);
        RecordEvent(vk, isDown: false);
    }

    private void RecordEvent(int vk, bool isDown)
    {
        var tMs = _stopwatch.Elapsed.TotalMilliseconds;
        var evt = new KeyEvent(vk, isDown ? "DOWN" : "UP", tMs);
        _events.Add(evt);

        var name = VkName(vk);
        EventLog.Items.Add($"#{_events.Count,4}  {name,-14} {evt.Direction,-4}  {tMs,10:F3} ms");

        // Auto-scroll to the latest entry.
        if (EventLog.Items.Count > 0)
            EventLog.ScrollIntoView(EventLog.Items[EventLog.Items.Count - 1]);

        UpdateStats();
        CaptureStatus.Text = $"Capturing… {_events.Count} events logged.";
    }

    private void UpdateStats()
    {
        if (_events.Count == 0)
        {
            StatsText.Text = "";
            return;
        }

        // Compute per-key dwell (up - down, FIFO matching by VK) and flight (next down - prev up).
        var pendingDown = new Dictionary<int, Queue<double>>();
        var dwells = new List<double>();
        var flights = new List<double>();
        KeyEvent? lastUp = null;

        foreach (var e in _events)
        {
            if (e.Direction == "DOWN")
            {
                if (lastUp != null)
                    flights.Add(e.TimeMs - lastUp.TimeMs);

                if (!pendingDown.ContainsKey(e.Vk))
                    pendingDown[e.Vk] = new Queue<double>();
                pendingDown[e.Vk].Enqueue(e.TimeMs);
            }
            else // UP
            {
                if (pendingDown.TryGetValue(e.Vk, out var q) && q.Count > 0)
                {
                    var downTime = q.Dequeue();
                    dwells.Add(e.TimeMs - downTime);
                }
                lastUp = e;
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Events: {_events.Count}  (KeyDown: {_events.Count(e => e.Direction == "DOWN")}, KeyUp: {_events.Count(e => e.Direction == "UP")})");

        if (dwells.Count > 0)
        {
            sb.AppendLine($"Dwell (ms):  n={dwells.Count}  min={dwells.Min():F1}  max={dwells.Max():F1}  mean={dwells.Average():F1}  stddev={StdDev(dwells):F1}");
            var inRange = dwells.Count(d => d is >= 30 and <= 200);
            sb.AppendLine($"  Dwell in [30,200] ms: {inRange}/{dwells.Count} ({(double)inRange / dwells.Count:P0})");
        }

        if (flights.Count > 0)
        {
            sb.AppendLine($"Flight (ms): n={flights.Count}  min={flights.Min():F1}  max={flights.Max():F1}  mean={flights.Average():F1}  stddev={StdDev(flights):F1}");
            var negative = flights.Count(f => f < 0);
            sb.AppendLine($"  Negative flight (overlap): {negative}/{flights.Count} ({(double)negative / flights.Count:P0})");
            var zeroOrNear = flights.Count(f => Math.Abs(f) < 1.0);
            sb.AppendLine($"  Near-zero flight (|f|<1 ms): {zeroOrNear}/{flights.Count} ({(double)zeroOrNear / flights.Count:P0})");
        }

        StatsText.Text = sb.ToString().TrimEnd();
    }

    private static double StdDev(List<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = values.Average();
        var sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / (values.Count - 1));
    }

    private static string VkName(int vk)
    {
        if (VkNames.TryGetValue(vk, out var name))
            return name;
        if (vk is >= 0x30 and <= 0x39)
            return $"D{(char)vk}";
        if (vk is >= 0x41 and <= 0x5A)
            return ((char)vk).ToString();
        return $"VK_{vk:X2}";
    }

    private void SaveCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_events.Count == 0)
        {
            SaveStatus.Text = "Nothing to save.";
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"inputprobe_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };

        if (dlg.ShowDialog() != true)
            return;

        using var writer = new StreamWriter(dlg.FileName);
        writer.WriteLine("index,vk,vk_name,event,t_ms");
        for (var i = 0; i < _events.Count; i++)
        {
            var ev = _events[i];
            writer.WriteLine($"{i + 1},{ev.Vk},{VkName(ev.Vk)},{ev.Direction},{ev.TimeMs:F3}");
        }

        SaveStatus.Text = $"Saved {_events.Count} events to {System.IO.Path.GetFileName(dlg.FileName)}";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _events.Clear();
        EventLog.Items.Clear();
        _stopwatch.Reset();
        StatsText.Text = "";
        SaveStatus.Text = "";
        CaptureStatus.Text = "Cleared. Ready — focus this window and start typing.";
    }

    private sealed record KeyEvent(int Vk, string Direction, double TimeMs);
}

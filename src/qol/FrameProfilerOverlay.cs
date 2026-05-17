using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;

namespace ToasterReskinLoader.qol;

// IMGUI overlay that tracks frame times, detects spikes, and helps isolate
// stutter sources. F3 = toggle overlay, F4 = cycle display mode,
// F5 = toggle CSV logging.
public class FrameProfilerOverlay : MonoBehaviour
{
    const int FRAME_HISTORY = 600;
    const float SPIKE_THRESHOLD_MS = 20f;
    const int SPIKE_LOG_MAX = 50;
    const float SUMMARY_INTERVAL = 5f;

    bool showOverlay = true;
    int displayMode = 0;
    readonly float[] frameTimes = new float[FRAME_HISTORY];
    int frameIndex = 0;
    int totalFrames = 0;

    readonly List<SpikeEntry> spikeLog = new List<SpikeEntry>();
    int spikeCount = 0;

    float minFrameTime = float.MaxValue;
    float maxFrameTime = 0f;
    float sumFrameTime = 0f;
    int statsFrameCount = 0;
    float lastSummaryTime = 0f;

    long lastTotalMemory = 0;
    float lastGcTime = 0f;
    int gcEventsInWindow = 0;
    readonly float[] gcTimestamps = new float[32];
    int gcTimestampIndex = 0;

    float lastSpikeTime = 0f;
    readonly float[] spikeIntervals = new float[32];
    int spikeIntervalIndex = 0;
    int spikeIntervalCount = 0;

    bool csvLogging = false;
    StringBuilder csvBuffer = new StringBuilder();
    int csvFlushCounter = 0;
    string csvPath;

    float monitorUpdateTimer = 0f;
    long monoHeapSize = 0;
    long monoUsedSize = 0;
    long totalAllocatedMemory = 0;
    long totalReservedMemory = 0;
    int currentGcCount = 0;

    Texture2D graphBg;
    Texture2D spikeLine;
    Texture2D barGreen;
    Texture2D barYellow;
    Texture2D barRed;
    GUIStyle labelStyle;
    GUIStyle headerStyle;
    GUIStyle boxStyle;
    bool stylesInitialized = false;

    struct SpikeEntry
    {
        public int FrameNumber;
        public float TimeMs;
        public float TimeSinceStart;
        public float IntervalSinceLast;
        public long MemoryDelta;
        public bool GcOccurred;
    }

    void Start()
    {
        csvPath = Application.persistentDataPath + "/frame_profiler_log.csv";
        CreateTextures();
        Plugin.Log($"[FrameProfiler] Overlay started. Spike threshold: {SPIKE_THRESHOLD_MS}ms");
        Plugin.Log($"[FrameProfiler] CSV log path: {csvPath}");
    }

    void CreateTextures()
    {
        graphBg = MakeTex(1, 1, new Color(0f, 0f, 0f, 0.75f));
        spikeLine = MakeTex(1, 1, new Color(1f, 0f, 0f, 0.5f));
        barGreen = MakeTex(1, 1, new Color(0.2f, 0.9f, 0.2f, 0.9f));
        barYellow = MakeTex(1, 1, new Color(0.9f, 0.9f, 0.2f, 0.9f));
        barRed = MakeTex(1, 1, new Color(0.9f, 0.2f, 0.2f, 0.9f));
    }

    void InitStyles()
    {
        if (stylesInitialized) return;
        stylesInitialized = true;

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            normal = { textColor = Color.white }
        };

        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.4f, 0.8f, 1f) }
        };

        boxStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = graphBg }
        };
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float dtMs = dt * 1000f;

        frameTimes[frameIndex] = dtMs;
        frameIndex = (frameIndex + 1) % FRAME_HISTORY;
        totalFrames++;

        statsFrameCount++;
        sumFrameTime += dtMs;
        if (dtMs < minFrameTime) minFrameTime = dtMs;
        if (dtMs > maxFrameTime) maxFrameTime = dtMs;

        long currentMemory = GC.GetTotalMemory(false);
        long memDelta = currentMemory - lastTotalMemory;
        int gcCount = GC.CollectionCount(0);
        bool gcOccurred = gcCount > currentGcCount;
        if (gcOccurred)
        {
            currentGcCount = gcCount;
            gcTimestamps[gcTimestampIndex] = Time.unscaledTime;
            gcTimestampIndex = (gcTimestampIndex + 1) % gcTimestamps.Length;
            gcEventsInWindow++;
            lastGcTime = Time.unscaledTime;
        }
        lastTotalMemory = currentMemory;

        if (dtMs > SPIKE_THRESHOLD_MS)
        {
            spikeCount++;
            float interval = Time.unscaledTime - lastSpikeTime;

            var entry = new SpikeEntry
            {
                FrameNumber = Time.frameCount,
                TimeMs = dtMs,
                TimeSinceStart = Time.unscaledTime,
                IntervalSinceLast = lastSpikeTime > 0 ? interval : 0f,
                MemoryDelta = memDelta,
                GcOccurred = gcOccurred
            };

            if (spikeLog.Count < SPIKE_LOG_MAX)
                spikeLog.Add(entry);
            else
                spikeLog[spikeCount % SPIKE_LOG_MAX] = entry;

            if (lastSpikeTime > 0)
            {
                spikeIntervals[spikeIntervalIndex] = interval;
                spikeIntervalIndex = (spikeIntervalIndex + 1) % spikeIntervals.Length;
                if (spikeIntervalCount < spikeIntervals.Length) spikeIntervalCount++;
            }

            lastSpikeTime = Time.unscaledTime;

            if (csvLogging)
            {
                csvBuffer.AppendLine($"{Time.frameCount},{dtMs:F2},{Time.unscaledTime:F3},{interval:F3},{memDelta},{gcOccurred},{currentMemory}");
                csvFlushCounter++;
                if (csvFlushCounter >= 10)
                {
                    FlushCsv();
                    csvFlushCounter = 0;
                }
            }
        }

        monitorUpdateTimer += dt;
        if (monitorUpdateTimer >= 0.5f)
        {
            monitorUpdateTimer = 0f;
            monoHeapSize = Profiler.GetMonoHeapSizeLong();
            monoUsedSize = Profiler.GetMonoUsedSizeLong();
            totalAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong();
            totalReservedMemory = Profiler.GetTotalReservedMemoryLong();
        }

        if (Time.unscaledTime - lastSummaryTime >= SUMMARY_INTERVAL)
        {
            PrintSummary();
            lastSummaryTime = Time.unscaledTime;
        }

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.f3Key.wasPressedThisFrame) showOverlay = !showOverlay;
            if (kb.f4Key.wasPressedThisFrame) displayMode = (displayMode + 1) % 3;
            if (kb.f5Key.wasPressedThisFrame)
            {
                csvLogging = !csvLogging;
                if (csvLogging)
                {
                    csvBuffer.Clear();
                    csvBuffer.AppendLine("Frame,FrameTimeMs,GameTime,IntervalSinceLast,MemoryDelta,GcOccurred,TotalMemory");
                    Plugin.Log($"[FrameProfiler] CSV logging ENABLED -> {csvPath}");
                }
                else
                {
                    FlushCsv();
                    Plugin.Log("[FrameProfiler] CSV logging DISABLED, file flushed.");
                }
            }
        }
    }

    void PrintSummary()
    {
        if (statsFrameCount == 0) return;

        float avg = sumFrameTime / statsFrameCount;
        Plugin.Log($"[FrameProfiler][PERF] frames={statsFrameCount} avg={avg:F1}ms min={minFrameTime:F1}ms max={maxFrameTime:F1}ms " +
                   $"spikes(>{SPIKE_THRESHOLD_MS}ms)={spikeCount} gc0={currentGcCount} " +
                   $"monoHeap={FormatBytes(monoHeapSize)} monoUsed={FormatBytes(monoUsedSize)}");

        if (spikeIntervalCount >= 3) AnalyzeSpikePattern();

        minFrameTime = float.MaxValue;
        maxFrameTime = 0f;
        sumFrameTime = 0f;
        statsFrameCount = 0;
        spikeCount = 0;
    }

    void AnalyzeSpikePattern()
    {
        float sum = 0f;
        int count = Math.Min(spikeIntervalCount, spikeIntervals.Length);
        for (int i = 0; i < count; i++) sum += spikeIntervals[i];
        float mean = sum / count;

        float variance = 0f;
        for (int i = 0; i < count; i++)
        {
            float diff = spikeIntervals[i] - mean;
            variance += diff * diff;
        }
        float stddev = (float)Math.Sqrt(variance / count);

        float regularity = mean > 0 ? (1f - stddev / mean) : 0f;
        string pattern = regularity > 0.7f ? "REGULAR (timer/tick suspected)" :
                         regularity > 0.4f ? "SEMI-REGULAR" : "IRREGULAR (likely GC or load)";

        Plugin.Log($"[FrameProfiler][PATTERN] spike interval: mean={mean:F2}s stddev={stddev:F2}s regularity={regularity:P0} -> {pattern}");

        if (regularity > 0.5f)
        {
            if (mean > 0.8f && mean < 1.3f)
                Plugin.Log("[FrameProfiler][PATTERN] ~1s interval matches: GameManager.Server_Tick, PlayerController ping, EdgegapManager poll");
            else if (mean > 1.8f && mean < 2.3f)
                Plugin.Log("[FrameProfiler][PATTERN] ~2s interval: possible doubled tick or GC gen1");
            else if (mean > 4.5f && mean < 5.5f)
                Plugin.Log("[FrameProfiler][PATTERN] ~5s interval matches: ChatManager timeout, UIAnnouncements hide, or GC gen2");
            else
                Plugin.Log($"[FrameProfiler][PATTERN] ~{mean:F1}s interval: no known game timer match - investigate custom systems");
        }
    }

    void OnGUI()
    {
        if (!showOverlay) return;
        InitStyles();

        switch (displayMode)
        {
            case 0: DrawGraphAndStats(); break;
            case 1: DrawSpikeLog(); break;
            case 2: DrawSystemMonitors(); break;
        }
    }

    void DrawGraphAndStats()
    {
        float panelW = 420f;
        float panelH = 240f;
        float x = Screen.width - panelW - 10f;
        float y = 10f;

        GUI.Box(new Rect(x, y, panelW, panelH), "", boxStyle);

        float graphX = x + 10f;
        float graphY = y + 25f;
        float graphW = panelW - 20f;
        float graphH = 120f;

        string serverIp = "";
        try
        {
            var conn = GlobalStateManager.ConnectionState.Connection;
            if (conn != null && conn.EndPoint != null)
                serverIp = $"  [{conn.EndPoint}]";
        }
        catch { }
        GUI.Label(new Rect(x + 10, y + 3, panelW, 22), $"Frame Profiler{serverIp} [F3 F4 F5]", headerStyle);

        GUI.DrawTexture(new Rect(graphX, graphY, graphW, graphH), graphBg);

        float thresholdY = graphY + graphH - (SPIKE_THRESHOLD_MS / 50f) * graphH;
        GUI.DrawTexture(new Rect(graphX, thresholdY, graphW, 1), spikeLine);

        int barCount = Math.Min(FRAME_HISTORY, (int)graphW);
        float barW = graphW / barCount;
        for (int i = 0; i < barCount; i++)
        {
            int idx = (frameIndex - barCount + i + FRAME_HISTORY) % FRAME_HISTORY;
            float ms = frameTimes[idx];
            float barH = Mathf.Clamp(ms / 50f * graphH, 1f, graphH);
            float barY = graphY + graphH - barH;

            Texture2D color = ms < 12f ? barGreen : ms < SPIKE_THRESHOLD_MS ? barYellow : barRed;
            GUI.DrawTexture(new Rect(graphX + i * barW, barY, Mathf.Max(barW - 0.5f, 1f), barH), color);
        }

        GUI.Label(new Rect(graphX + graphW + 2, graphY - 2, 50, 18), "50ms", labelStyle);
        GUI.Label(new Rect(graphX + graphW + 2, thresholdY - 8, 50, 18), $"{SPIKE_THRESHOLD_MS}ms", labelStyle);
        GUI.Label(new Rect(graphX + graphW + 2, graphY + graphH - 14, 50, 18), "0", labelStyle);

        float statsY = graphY + graphH + 5f;
        float currentMs = frameTimes[(frameIndex - 1 + FRAME_HISTORY) % FRAME_HISTORY];
        float fps = currentMs > 0 ? 1000f / currentMs : 0;

        float[] sorted = new float[Math.Min(totalFrames, FRAME_HISTORY)];
        int sortCount = sorted.Length;
        for (int i = 0; i < sortCount; i++)
            sorted[i] = frameTimes[(frameIndex - sortCount + i + FRAME_HISTORY) % FRAME_HISTORY];
        Array.Sort(sorted);
        float onePercentLow = sortCount > 0 ? 1000f / sorted[sortCount - 1 - sortCount / 100] : 0;

        GUI.Label(new Rect(x + 10, statsY, panelW, 18),
            $"FPS: {fps:F0}  Frame: {currentMs:F1}ms  1%Low: {onePercentLow:F0}fps", labelStyle);
        GUI.Label(new Rect(x + 10, statsY + 18, panelW, 18),
            $"Spikes(>{SPIKE_THRESHOLD_MS}ms): {spikeCount}  GC0: {currentGcCount}  " +
            $"Heap: {FormatBytes(monoHeapSize)}", labelStyle);

        float timeSinceGc = Time.unscaledTime - lastGcTime;
        string gcAge = lastGcTime > 0 ? $"{timeSinceGc:F1}s ago" : "none";
        GUI.Label(new Rect(x + 10, statsY + 36, panelW, 18),
            $"Last GC: {gcAge}  Mono used: {FormatBytes(monoUsedSize)}", labelStyle);

        if (spikeIntervalCount >= 3)
        {
            float sum = 0f;
            int c = Math.Min(spikeIntervalCount, spikeIntervals.Length);
            for (int i = 0; i < c; i++) sum += spikeIntervals[i];
            float mean = sum / c;
            GUI.Label(new Rect(x + 10, statsY + 54, panelW, 18),
                $"Spike pattern: ~{mean:F2}s avg interval ({c} samples)", labelStyle);
        }
    }

    void DrawSpikeLog()
    {
        float panelW = 520f;
        float panelH = 340f;
        float x = Screen.width - panelW - 10f;
        float y = 10f;

        GUI.Box(new Rect(x, y, panelW, panelH), "", boxStyle);
        GUI.Label(new Rect(x + 10, y + 3, panelW, 22), "Spike Log [F4:mode]", headerStyle);

        float ly = y + 28f;
        GUI.Label(new Rect(x + 10, ly, panelW, 18),
            "Frame      Time     Since    Interval  MemDelta    GC", labelStyle);
        ly += 18f;

        int count = Math.Min(spikeLog.Count, 16);
        int start = Math.Max(0, spikeLog.Count - count);
        for (int i = start; i < spikeLog.Count; i++)
        {
            var s = spikeLog[i];
            string gc = s.GcOccurred ? "YES" : "   ";
            string interval = s.IntervalSinceLast > 0 ? $"{s.IntervalSinceLast,7:F2}s" : "     --";
            GUI.Label(new Rect(x + 10, ly, panelW, 18),
                $"{s.FrameNumber,7}  {s.TimeMs,7:F1}ms  {s.TimeSinceStart,6:F1}s  {interval}  {FormatBytesSigned(s.MemoryDelta),9}  {gc}",
                labelStyle);
            ly += 17f;
        }
    }

    void DrawSystemMonitors()
    {
        float panelW = 420f;
        float panelH = 220f;
        float x = Screen.width - panelW - 10f;
        float y = 10f;

        GUI.Box(new Rect(x, y, panelW, panelH), "", boxStyle);
        GUI.Label(new Rect(x + 10, y + 3, panelW, 22), "System Monitor [F4:mode]", headerStyle);

        float ly = y + 28f;
        int lineH = 19;

        GUI.Label(new Rect(x + 10, ly, panelW, 18), $"Mono Heap:          {FormatBytes(monoHeapSize)}", labelStyle); ly += lineH;
        GUI.Label(new Rect(x + 10, ly, panelW, 18), $"Mono Used:          {FormatBytes(monoUsedSize)}", labelStyle); ly += lineH;
        GUI.Label(new Rect(x + 10, ly, panelW, 18), $"Unity Allocated:    {FormatBytes(totalAllocatedMemory)}", labelStyle); ly += lineH;
        GUI.Label(new Rect(x + 10, ly, panelW, 18), $"Unity Reserved:     {FormatBytes(totalReservedMemory)}", labelStyle); ly += lineH;
        GUI.Label(new Rect(x + 10, ly, panelW, 18), $"GC Gen0 Count:      {GC.CollectionCount(0)}", labelStyle); ly += lineH;
        GUI.Label(new Rect(x + 10, ly, panelW, 18), $"GC Gen1 Count:      {GC.CollectionCount(1)}", labelStyle); ly += lineH;
        GUI.Label(new Rect(x + 10, ly, panelW, 18), $"GC Gen2 Count:      {GC.CollectionCount(2)}", labelStyle); ly += lineH;
        GUI.Label(new Rect(x + 10, ly, panelW, 18), $"Total Frame Count:  {Time.frameCount}", labelStyle); ly += lineH;
        GUI.Label(new Rect(x + 10, ly, panelW, 18), $"Time.timeScale:     {Time.timeScale:F2}", labelStyle); ly += lineH;
    }

    void FlushCsv()
    {
        if (csvBuffer.Length == 0) return;
        try
        {
            System.IO.File.AppendAllText(csvPath, csvBuffer.ToString());
            csvBuffer.Clear();
        }
        catch (Exception e)
        {
            Plugin.LogError($"[FrameProfiler] CSV write failed: {e.Message}");
            csvLogging = false;
        }
    }

    public void OnDestroy()
    {
        if (csvLogging) FlushCsv();
        if (graphBg != null) Destroy(graphBg);
        if (spikeLine != null) Destroy(spikeLine);
        if (barGreen != null) Destroy(barGreen);
        if (barYellow != null) Destroy(barYellow);
        if (barRed != null) Destroy(barRed);
    }

    static Texture2D MakeTex(int w, int h, Color col)
    {
        var pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = col;
        var tex = new Texture2D(w, h);
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1048576) return $"{bytes / 1024f:F1} KB";
        return $"{bytes / 1048576f:F1} MB";
    }

    static string FormatBytesSigned(long bytes)
    {
        string sign = bytes >= 0 ? "+" : "";
        if (Math.Abs(bytes) < 1024) return $"{sign}{bytes} B";
        if (Math.Abs(bytes) < 1048576) return $"{sign}{bytes / 1024f:F0} KB";
        return $"{sign}{bytes / 1048576f:F1} MB";
    }
}

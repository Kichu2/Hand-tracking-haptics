using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace HapticsTutorial
{
    /// <summary>
    /// Lightweight evaluation logger: timestamps, drop counts, and per-stage completion times.
    /// Writes to persistentDataPath so Quest playtest files can be pulled via USB later.
    /// </summary>
    public class SessionLogger : MonoBehaviour
    {
        public static SessionLogger Instance { get; private set; }

        [SerializeField] string filePrefix = "haptics_session";

        string _filePath;
        readonly StringBuilder _buffer = new StringBuilder();
        int _dropCount;
        DateTime _sessionStart;

        public int DropCount => _dropCount;
        public string FilePath => _filePath;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _sessionStart = DateTime.Now;
            var stamp = _sessionStart.ToString("yyyyMMdd_HHmmss");
            _filePath = Path.Combine(Application.persistentDataPath, $"{filePrefix}_{stamp}.csv");

            _buffer.AppendLine("timestamp_iso,elapsed_sec,event,stage,detail,drop_count");
            Log("session_start", "", "product=" + Application.productName);
            Debug.Log($"[Haptics Tutorial] Logging to {_filePath}");
        }

        void OnDestroy()
        {
            if (Instance == this)
                Flush();
        }

        void OnApplicationPause(bool pause)
        {
            if (pause)
                Flush();
        }

        void OnApplicationQuit()
        {
            Log("session_end", "", $"drops={_dropCount}");
            Flush();
        }

        public void Log(string eventName, string stage = "", string detail = "")
        {
            var now = DateTime.Now;
            var elapsed = (now - _sessionStart).TotalSeconds.ToString("F3");
            var line =
                $"{now:o},{elapsed},{Escape(eventName)},{Escape(stage)},{Escape(detail)},{_dropCount}";
            _buffer.AppendLine(line);
            Debug.Log($"[Haptics Tutorial] {eventName} stage={stage} {detail}");
            Flush();
        }

        public void RecordDrop(string stage)
        {
            _dropCount++;
            Log("drop", stage, $"count={_dropCount}");
        }

        public void RecordStageStart(string stage)
        {
            Log("stage_start", stage);
        }

        public void RecordStageComplete(string stage, float durationSeconds)
        {
            Log("stage_complete", stage, $"duration_sec={durationSeconds:F3}");
        }

        public void Flush()
        {
            try
            {
                File.WriteAllText(_filePath, _buffer.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Haptics Tutorial] Failed to write log: {ex.Message}");
            }
        }

        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            if (value.IndexOfAny(new[] { ',', '"', '\n' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Interrogation.Runtime
{
    public class InterrogationDebugRunner : MonoBehaviour
    {
        public TextAsset CSVFile; // Drag and drop the exported CSV here

        private Dictionary<string, RuntimeNode> _graph = new Dictionary<string, RuntimeNode>();
        private RuntimeNode _currentNode;

        private class RuntimeNode
        {
            public string ID;
            public string Type;
            public string Speaker;
            public string Content;
            public Dictionary<string, string> Options = new Dictionary<string, string>(); // Text -> TargetID
            public string NextNodeID; // For linear dialogue
            public string SilenceTargetID;
            public string TimelineAsset;
            public bool IsThreatExit;
        }

        void Start()
        {
            if (CSVFile == null)
            {
                Debug.LogError("Please assign a CSV TextAsset to the InterrogationDebugRunner.");
                return;
            }

            ParseCSV(CSVFile.text);
            StartGraph();
        }

        void Update()
        {
            if (_currentNode == null) return;

            // F Key: Advance Dialogue or move to linear next
            if (Keyboard.current.fKey.wasPressedThisFrame)
            {
                if (_currentNode.Type == "DIALOGUE" || _currentNode.Type == "START")
                {
                    if (!string.IsNullOrEmpty(_currentNode.NextNodeID))
                    {
                        Traverse(_currentNode.NextNodeID);
                    }
                    else
                    {
                        Debug.Log("End of flow (No Next Node).");
                    }
                }
            }

            // 1 Key: Option 1 / Yes
            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                HandleOptionInput(0);
            }

            // 2 Key: Option 2 / No
            if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                HandleOptionInput(1);
            }
        }

        private void HandleOptionInput(int index)
        {
            if (_currentNode.Type != "CHOICE") return;

            var options = new List<string>(_currentNode.Options.Keys);
            if (index < options.Count)
            {
                string text = options[index];
                string targetID = _currentNode.Options[text];
                Debug.Log($"User chose: {text}");
                Traverse(targetID);
            }
            else
            {
                Debug.Log($"Invalid Option {index + 1}");
            }
        }

        private void Traverse(string id)
        {
            if (!_graph.ContainsKey(id))
            {
                Debug.LogError($"Node ID {id} not found!");
                return;
            }

            _currentNode = _graph[id];
            PrintNodeInfo(_currentNode);
            
            // Auto-play timeline logic would go here
            if (!string.IsNullOrEmpty(_currentNode.TimelineAsset))
            {
                Debug.Log($"[PLAY TIMELINE]: {_currentNode.TimelineAsset}");
            }

            if (_currentNode.IsThreatExit)
            {
                Debug.LogWarning("!!! THREAT EXIT TRIGGERED !!!");
            }

            if (_currentNode.Type == "END")
            {
                Debug.Log("--- INTERROGATION ENDED ---");
                _currentNode = null;
            }
        }

        private void StartGraph()
        {
            // Find Start Node - usually ID "1" or type START
            foreach (var node in _graph.Values)
            {
                if (node.Type == "START")
                {
                    Traverse(node.ID);
                    return;
                }
            }
            Debug.LogError("No START node found.");
        }

        private void PrintNodeInfo(RuntimeNode node)
        {
            string log = $"<b>[{node.Type}]</b> ID:{node.ID}";
            
            if (!string.IsNullOrEmpty(node.Speaker)) log += $" Speaker:{node.Speaker}";
            
            log += $"\n\"{node.Content}\"";

            if (node.Type == "CHOICE")
            {
                int i = 1;
                foreach (var opt in node.Options.Keys)
                {
                    log += $"\n  [{i}] {opt}";
                    i++;
                }
                if (!string.IsNullOrEmpty(node.SilenceTargetID))
                {
                     log += $"\n  (Silence logic available via internal timer/check)";
                }
            }
            else if (node.Type == "DIALOGUE" || node.Type == "START")
            {
                log += "\n  [F] Next";
            }

            Debug.Log(log);
        }

        private void ParseCSV(string csvText)
        {
            var lines = csvText.Split('\n');
            // Skip header (index 0)
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Simple comma split (assuming no commas in text for this debug prototype, 
                // or use a proper CSV parser if needed later. The sanitizer added quotes, so we might need regex if complex)
                // For this test, we'll try simplistic split first, aware of its limitations.
                
                // Proper split handling quotes:
                var parts = SplitCSVLine(line);
                if (parts.Count < 8) continue;

                var node = new RuntimeNode
                {
                    ID = parts[0],
                    Type = parts[1],
                    Speaker = parts[2],
                    Content = parts[3].Replace("\"\"", "\""), // Unescape quotes
                    SilenceTargetID = parts[5],
                    TimelineAsset = parts[6],
                    IsThreatExit = parts[7] == "TRUE"
                };

                // Parse Options column: "Text:TargetID|Next:TargetID"
                string optionsRaw = parts[4];
                if (!string.IsNullOrEmpty(optionsRaw))
                {
                    var opts = optionsRaw.Split('|');
                    foreach (var opt in opts)
                    {
                        var kv = opt.Split(':');
                        if (kv.Length == 2)
                        {
                            string key = kv[0];
                            string val = kv[1];
                            if (key == "Next")
                            {
                                node.NextNodeID = val;
                            }
                            else
                            {
                                node.Options[key] = val;
                            }
                        }
                    }
                }

                _graph[node.ID] = node;
            }
            Debug.Log($"Loaded {_graph.Count} nodes.");
        }

        private List<string> SplitCSVLine(string line)
        {
            // Basic regex or state machine for CSV parsing
            var result = new List<string>();
            bool inQuotes = false;
            string current = "";

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current);
                    current = "";
                }
                else
                {
                    current += c;
                }
            }
            result.Add(current);

            // Clean up surrounding quotes
            for(int i=0; i<result.Count; i++)
            {
                string s = result[i];
                if (s.Length >= 2 && s.StartsWith("\"") && s.EndsWith("\""))
                {
                    result[i] = s.Substring(1, s.Length - 2);
                }
            }

            return result;
        }
    }
}

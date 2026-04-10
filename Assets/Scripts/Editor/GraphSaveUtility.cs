using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Interrogation.Editor
{
    public class GraphSaveUtility
    {
        private InterrogationGraphView _targetGraphView;

        private List<Edge> Edges => _targetGraphView.edges.ToList();
        private List<InterrogationNode> Nodes => _targetGraphView.nodes.ToList().Cast<InterrogationNode>().ToList();

        public static GraphSaveUtility GetInstance(InterrogationGraphView graphView)
        {
            return new GraphSaveUtility
            {
                _targetGraphView = graphView
            };
        }

        public void SaveGraph(string fileName)
        {
            var graphData = new InterrogationGraphSaveData();

            foreach (var node in Nodes)
            {
                // Serialize Node
                var nodeData = new NodeSaveData
                {
                    GUID = node.GUID,
                    Type = GetNodeType(node),
                    Position = node.GetPosition().position,
                    NodeText = node.NodeText,
                    IsThreatExit = node.IsThreatExit,
                    TimelineAsset = node.TimelineAsset
                };

                if (node is DialogueNode dNode) nodeData.SpeakerID = dNode.SpeakerID;
                if (node is ChoiceNode cNode) nodeData.HasSilenceOption = cNode.HasSilenceOption;

                graphData.Nodes.Add(nodeData);
            }

            foreach (var edge in Edges)
            {
                var inputNode = edge.input.node as InterrogationNode;
                var outputNode = edge.output.node as InterrogationNode;

                graphData.Connections.Add(new ConnectionSaveData
                {
                    OutputNodeGUID = outputNode.GUID,
                    InputNodeGUID = inputNode.GUID,
                    PortName = edge.output.portName
                });
            }

            if (!System.IO.Directory.Exists("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }
            
            string path = EditorUtility.SaveFilePanel("Save Graph", "Assets/Resources", fileName, "json");
            if (string.IsNullOrEmpty(path)) return;

            string json = JsonUtility.ToJson(graphData, true);
            System.IO.File.WriteAllText(path, json);
            AssetDatabase.Refresh();
            Debug.Log($"Graph saved to {path}");
        }

        public void LoadGraph()
        {
            string path = EditorUtility.OpenFilePanel("Load Graph", "Assets/Resources", "json");
            if (string.IsNullOrEmpty(path)) return;

            string json = System.IO.File.ReadAllText(path);
            var graphData = JsonUtility.FromJson<InterrogationGraphSaveData>(json);

            ClearGraph();
            GenerateNodes(graphData);
            ConnectNodes(graphData);
        }

        public void LoadGraphFromCSV()
        {
            string path = EditorUtility.OpenFilePanel("Import Graph from CSV", "Assets", "csv");
            if (string.IsNullOrEmpty(path)) return;

            string csvText = System.IO.File.ReadAllText(path);
            
            ClearGraph();
            ReconstructGraphFromCSV(csvText);
        }

        private void ReconstructGraphFromCSV(string csvText)
        {
            var lines = csvText.Split('\n');
            var csvIdToNodeMap = new Dictionary<string, InterrogationNode>();
            var nodeConnections = new Dictionary<InterrogationNode, List<string>>(); // Node -> List of "PortName:TargetID" strings

            // specific layout variables
            Vector2 currentPos = new Vector2(100, 200);
            float verticalSpacing = 300;
            float horizontalSpacing = 400;
            // Simple approach: Dialogue/Choice in a grid? Or just vertical column for now?
            // Let's do a vertical column, maybe staggered?
            
            // Skip header
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var parts = SplitCSVLine(line);
                if (parts.Count < 8) continue;
                
                // Parse Data
                string id = parts[0];
                string type = parts[1]; // DIALOGUE, CHOICE, START, END
                string speaker = parts[2];
                string content = parts[3].Replace("\"\"", "\"");
                string optionsRaw = parts[4];
                string silenceTargetID = parts[5];
                string timeline = parts[6];
                bool isThreat = parts[7] == "TRUE";

                // Map CSV Type to Editor Type
                string editorType = "Dialogue";
                if (type == "START") editorType = "Start";
                else if (type == "END") editorType = "End";
                else if (type == "CHOICE") editorType = "Choice";
                else if (type == "DIALOGUE") editorType = "Dialogue";
                
                // Create Node
                var node = _targetGraphView.CreateInterrogationNode(editorType, currentPos);
                
                // Layout increment
                currentPos.y += verticalSpacing;
                if (i % 10 == 0) // Wrap to next column every 10 nodes
                {
                    currentPos.y = 200;
                    currentPos.x += horizontalSpacing;
                }

                // Apply Data
                node.NodeText = content;
                node.IsThreatExit = isThreat;
                node.TimelineAsset = timeline;

                // Specifics
                if (node is DialogueNode dNode)
                {
                    dNode.SpeakerID = speaker;
                    // Update UI
                    var textFields = dNode.mainContainer.Query<TextField>().ToList();
                    foreach(var tf in textFields)
                    {
                        if (tf.label == "Dialogue:") tf.SetValueWithoutNotify(content);
                        if (tf.label == "Speaker ID:") tf.SetValueWithoutNotify(speaker);
                        if (tf.label == "Timeline Asset:") tf.SetValueWithoutNotify(timeline);
                    }
                }
                else if (node is ChoiceNode cNode)
                {
                     // Update UI
                     var textFields = cNode.mainContainer.Query<TextField>().ToList();
                     foreach(var tf in textFields)
                     {
                        if (tf.label == "Timeline Asset:") tf.SetValueWithoutNotify(timeline);
                     }
                }
                
                // Common Toggles
                var toggles = node.mainContainer.Query<Toggle>().ToList();
                foreach(var t in toggles)
                {
                    if (t.label == "Is Threat Exit") t.SetValueWithoutNotify(isThreat);
                }

                _targetGraphView.AddElement(node);
                csvIdToNodeMap[id] = node;

                // Validating Options for connection phase
                var connectionList = new List<string>();
                if (!string.IsNullOrEmpty(optionsRaw))
                {
                    var opts = optionsRaw.Split('|');
                    connectionList.AddRange(opts);
                }
                
                // Silence logic
                if (!string.IsNullOrEmpty(silenceTargetID))
                {
                    connectionList.Add($"Silence:{silenceTargetID}");
                    if (node is ChoiceNode cNode)
                    {
                        cNode.HasSilenceOption = true;
                        _targetGraphView.ToggleSilencePort(cNode, true);
                        // Update toggle UI
                        foreach(var t in toggles)
                        {
                            if (t.label == "Has Silence Option") t.SetValueWithoutNotify(true);
                        }
                    }
                }

                nodeConnections[node] = connectionList;
            }

            // Phase 2: Connections
            foreach (var kvp in nodeConnections)
            {
                var node = kvp.Key;
                var connections = kvp.Value;

                foreach (var connString in connections)
                {
                    // Format: "PortName:TargetID" 
                    // Note: In Export, we did "OptionText:TargetID" or "Next:TargetID"
                    // So we split by LAST colon? Or first? Option text might have colons?
                    // Safe assumption for now: LastIndexOf(':')
                    int splitIdx = connString.LastIndexOf(':');
                    if (splitIdx == -1) continue;

                    string portName = connString.Substring(0, splitIdx);
                    string targetID = connString.Substring(splitIdx + 1);

                    if (csvIdToNodeMap.ContainsKey(targetID))
                    {
                        var targetNode = csvIdToNodeMap[targetID];
                        Port outputPort = null;

                        // Identify Port
                        // If "Next", standard port
                        // If "Silence", specific port
                        // If other, likely Dynamic Choice Port
                        
                        var existingPorts = node.outputContainer.Query<Port>().ToList();
                        outputPort = existingPorts.FirstOrDefault(p => p.portName == portName);

                        if (outputPort == null && node is ChoiceNode cNode && portName != "Silence" && portName != "Next")
                        {
                            // It's a choice option text. Create dynamic port with that name.
                            _targetGraphView.AddChoicePort(cNode, portName);
                            // Refresh
                            existingPorts = node.outputContainer.Query<Port>().ToList();
                            outputPort = existingPorts.FirstOrDefault(p => p.portName == portName);
                        }

                        // Last resort for linear
                        if (outputPort == null && !(node is ChoiceNode)) outputPort = existingPorts.FirstOrDefault();

                        if (outputPort != null)
                        {
                             var inputPort = (Port)targetNode.inputContainer[0];
                             var edge = outputPort.ConnectTo(inputPort);
                             _targetGraphView.AddElement(edge);
                        }
                    }
                }
            }
        }

        private List<string> SplitCSVLine(string line)
        {
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

        private void ClearGraph()
        {
            _targetGraphView.ClearGraph();
        }

        private void GenerateNodes(InterrogationGraphSaveData data)
        {
            foreach (var nodeData in data.Nodes)
            {
                var node = _targetGraphView.CreateInterrogationNode(nodeData.Type, nodeData.Position);
                
                // Restore properties
                node.GUID = nodeData.GUID;
                node.NodeText = nodeData.NodeText;
                node.IsThreatExit = nodeData.IsThreatExit;
                node.TimelineAsset = nodeData.TimelineAsset;

                // Sync UI: Common fields
                var timelineField = node.mainContainer.Q<TextField>(null, "unity-text-field"); // By default TextField might not have name.
                // Assuming order or label text?
                // Let's rely on finding by label text if possible, but VisualElement query for label is hard on composite field.
                // Iterating children:
                var textFields = node.mainContainer.Query<TextField>().ToList();
                // Common ones appended last in CreateInterrogationNode: TimelineAsset
                // But Structure varies by node type.
                // Dialogue: [Dialogue], [Speaker], [Timeline]
                // Choice: [SilenceToggle], [Timeline]
                
                // Let's set values generically where we can find them.
                foreach(var tf in textFields)
                {
                    if (tf.label == "Timeline Asset:") tf.SetValueWithoutNotify(nodeData.TimelineAsset);
                }

                var toggles = node.mainContainer.Query<Toggle>().ToList();
                foreach(var t in toggles)
                {
                    if (t.label == "Is Threat Exit") t.SetValueWithoutNotify(nodeData.IsThreatExit);
                }

                if (node is DialogueNode dNode)
                {
                    dNode.SpeakerID = nodeData.SpeakerID;
                    foreach(var tf in textFields)
                    {
                        if (tf.label == "Dialogue:") tf.SetValueWithoutNotify(nodeData.NodeText);
                        if (tf.label == "Speaker ID:") tf.SetValueWithoutNotify(nodeData.SpeakerID);
                    }
                }
                else if (node is ChoiceNode cNode)
                {
                   cNode.HasSilenceOption = nodeData.HasSilenceOption;
                   foreach(var t in toggles)
                   {
                       if (t.label == "Has Silence Option") 
                       {
                           t.SetValueWithoutNotify(cNode.HasSilenceOption);
                           // Manually trigger the port logic if it was true
                           if(cNode.HasSilenceOption)
                           {
                               // We need to re-add the port.
                               // But check if it already exists? (CreateInterrogationNode default is false)
                               // ToggleSilencePort handles re-adding.
                               // Wait, calling ToggleSilencePort adds the port AND sets state.
                               // Since we set ValueWithoutNotify, the listener didn't fire.
                               // So we must call the logic manually.
                               _targetGraphView.ToggleSilencePort(cNode, true);
                           }
                       }
                   }
                }

                _targetGraphView.AddElement(node);
            }
        }

        private void ConnectNodes(InterrogationGraphSaveData data)
        {
            var nodes = Nodes; 

            foreach (var conn in data.Connections)
            {
                var outputNode = nodes.FirstOrDefault(n => n.GUID == conn.OutputNodeGUID);
                var inputNode = nodes.FirstOrDefault(n => n.GUID == conn.InputNodeGUID);

                if (outputNode == null || inputNode == null) continue;

                Port outputPort = null;
                var existingPorts = outputNode.outputContainer.Query<Port>().ToList();
                
                // Try finding directly
                outputPort = existingPorts.FirstOrDefault(p => p.portName == conn.PortName);

                // If missing (Choice dynamic ports)
                if (outputPort == null && outputNode is ChoiceNode cNode)
                {
                    // Create it!
                    _targetGraphView.AddChoicePort(cNode, conn.PortName);
                    // Refresh ports list
                    existingPorts = outputNode.outputContainer.Query<Port>().ToList();
                    outputPort = existingPorts.FirstOrDefault(p => p.portName == conn.PortName);
                }

                // If still null (e.g., standard "Next" port mismatch?), try first output for standard nodes
                if (outputPort == null && !(outputNode is ChoiceNode))
                {
                    outputPort = existingPorts.FirstOrDefault();
                }

                if (outputPort != null)
                {
                    // Find input port (usually just one "Input")
                    var inputPort = (Port)inputNode.inputContainer[0]; // risky if index 0 isn't valid, but for our nodes it is.
                    
                    var edge = outputPort.ConnectTo(inputPort);
                    _targetGraphView.AddElement(edge);
                }
            }
        }

        private string GetNodeType(InterrogationNode node)
        {
            if (node is StartNode) return "Start"; // Case sensitive matching logic in CreateInterrogationNode usually takes Simple name "Start"
            // Wait, CreateInterrogationNode uses: "Dialogue", "Choice", "End". Start is EntryPoint.
            // But we treat Start as a node type now?
            // CreateInterrogationNode logic:
            /*
            public InterrogationNode CreateInterrogationNode(string type, Vector2 position)
            {
                switch (type) ...
            }
            */
            // It does NOT handle "Start". GenerateEntryPointNode handles Start.
            // We need to handle Start node loading differently or add case to CreateInterrogationNode?
            // Let's add Start case to CreateInterrogationNode or handle it in GenerateNodes.
            // Actually, best to have CreateInterrogationNode handle it if we want generic loading.
            
            // However, typical GraphView workflow has one fixed entry point.
            // If we are saving/loading *everything*, we might duplicate the entry point if we are not careful.
            // ClearGraph() wipes everything.
            // So we need to be able to recreate StartNode.
            
            if (node is StartNode) return "Start";
            if (node is EndNode) return "End";
            if (node is DialogueNode) return "Dialogue";
            if (node is ChoiceNode) return "Choice";
            return "Dialogue";
        }
    }

    [System.Serializable]
    public class InterrogationGraphSaveData
    {
        public List<NodeSaveData> Nodes = new List<NodeSaveData>();
        public List<ConnectionSaveData> Connections = new List<ConnectionSaveData>();
    }

    [System.Serializable]
    public class NodeSaveData
    {
        public string GUID;
        public string Type;
        public Vector2 Position;
        public string NodeText;
        public string SpeakerID;
        public bool IsThreatExit;
        public string TimelineAsset;
        public bool HasSilenceOption;
    }

    [System.Serializable]
    public class ConnectionSaveData
    {
        public string OutputNodeGUID;
        public string InputNodeGUID;
        public string PortName;
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Interrogation.Editor
{
    public static class CSVExporter
    {
        public static void Export(InterrogationGraphView graphView)
        {
            var path = EditorUtility.SaveFilePanel("Export to CSV", "", "InterrogationGraph", "csv");
            if (string.IsNullOrEmpty(path)) return;

            var nodes = graphView.nodes.ToList().Cast<InterrogationNode>().ToList();
            var edges = graphView.edges.ToList();

            // 1. Assign deterministic integer IDs to nodes for the CSV
            // We'll store a mapping of NodeGUID -> SimpleID (e.g., "1", "2", "3")
            // To make it stable-ish, we could sort by position, but for now just order of creation/list.
            var nodeToIDMap = new Dictionary<string, string>();
            int idCounter = 1;
            
            // Sort nodes by vertical position to have a somewhat logical flow in CSV
            nodes.Sort((a, b) => a.GetPosition().y.CompareTo(b.GetPosition().y));

            foreach (var node in nodes)
            {
                nodeToIDMap[node.GUID] = idCounter.ToString();
                idCounter++;
            }

            var csvContent = new System.Text.StringBuilder();
            // Header
            csvContent.AppendLine("ID,Type,Speaker,Content,Options(Text:TargetID),SilenceTargetID,TimelineAsset,IsThreatExit");

            foreach (var node in nodes)
            {
                string id = nodeToIDMap[node.GUID];
                string type = GetNodeType(node);
                string speaker = "";
                string content = node.NodeText; // Dialogue text or Prompt
                string options = "";
                string silenceTargetID = "";
                string timeline = node.TimelineAsset;
                string isThreat = node.IsThreatExit ? "TRUE" : "FALSE";
                string nextID = "";

                if (node is DialogueNode dNode)
                {
                    speaker = dNode.SpeakerID;
                    // Find connection from "Next" port
                    var nextPort = node.outputContainer.Q<Port>("Next"); // Actually name is empty or "Next"
                    // In CreateInterrogationNode: outputPort.portName = "Next"
                    
                    nextID = FindTargetID(node, "Next", edges, nodeToIDMap);
                    // For Dialogue, we can put NextID in the "Options" column as "Next:ID" or just assume linear.
                    // Let's use Options column for single next pointer as "Next:ID" for consistency? 
                    // Or maybe the user logic expects a separate column?
                    // User Request 4: "Output: ... connection info ... column".
                    // Let's format NextID in Options column as "Next:ID" or similar if it's linear.
                    if (!string.IsNullOrEmpty(nextID))
                    {
                        options = $"Next:{nextID}";
                    }
                }
                else if (node is ChoiceNode cNode)
                {
                    // Iterate all output ports
                    var outputPorts = node.outputContainer.Query<Port>().ToList();
                    var optionList = new List<string>();

                    foreach (var port in outputPorts)
                    {
                        if (port.portName == "Silence")
                        {
                            silenceTargetID = FindTargetID(node, port.portName, edges, nodeToIDMap);
                        }
                        else
                        {
                            var targetID = FindTargetID(node, port.portName, edges, nodeToIDMap);
                            if (!string.IsNullOrEmpty(targetID))
                            {
                                // Format: "OptionText:TargetID"
                                // The portName is the Option Text
                                optionList.Add($"{port.portName}:{targetID}");
                            }
                        }
                    }
                    options = string.Join("|", optionList);
                }
                else if (node is StartNode)
                {
                     nextID = FindTargetID(node, "Next", edges, nodeToIDMap);
                     if (!string.IsNullOrEmpty(nextID)) options = $"Next:{nextID}";
                }

                // Sanitize CSV strings
                content = Sanitize(content);
                speaker = Sanitize(speaker);
                options = Sanitize(options);

                csvContent.AppendLine($"{id},{type},{speaker},{content},{options},{silenceTargetID},{timeline},{isThreat}");
            }

            File.WriteAllText(path, csvContent.ToString());
            Debug.Log($"Exported CSV to {path}");
        }

        private static string GetNodeType(InterrogationNode node)
        {
            if (node is StartNode) return "START";
            if (node is EndNode) return "END";
            if (node is DialogueNode) return "DIALOGUE";
            if (node is ChoiceNode) return "CHOICE";
            return "UNKNOWN";
        }

        private static string FindTargetID(InterrogationNode node, string portName, List<Edge> edges, Dictionary<string, string> map)
        {
            // Find edge connecting from this node's port
            // Edge.output is the port on the start node
            var relevantEdges = edges.Where(e => e.output.node == node && e.output.portName == portName).ToList();
            
            if (relevantEdges.Count > 0)
            {
                var targetNode = relevantEdges[0].input.node as InterrogationNode;
                if (targetNode != null && map.ContainsKey(targetNode.GUID))
                {
                    return map[targetNode.GUID];
                }
            }
            return "";
        }

        private static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            if (input.Contains(",") || input.Contains("\"") || input.Contains("\n"))
            {
                return $"\"{input.Replace("\"", "\"\"")}\"";
            }
            return input;
        }
    }
}

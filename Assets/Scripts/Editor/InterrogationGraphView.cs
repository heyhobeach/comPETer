using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;

namespace Interrogation.Editor
{
    public class InterrogationGraphView : GraphView
    {
        public InterrogationGraphView()
        {
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            AddElement(GenerateEntryPointNode());
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatiblePorts = new List<Port>();

            ports.ForEach((port) =>
            {
                if (startPort != port && startPort.node != port.node)
                {
                    compatiblePorts.Add(port);
                }
            });

            return compatiblePorts;
        }

        private Port GeneratePort(InterrogationNode node, Direction portDirection, Port.Capacity capacity = Port.Capacity.Single)
        {
            return node.InstantiatePort(Orientation.Horizontal, portDirection, capacity, typeof(float)); // Type doesn't matter much for flow
        }

        private StartNode GenerateEntryPointNode()
        {
            var node = new StartNode
            {
                title = "START",
                GUID = Guid.NewGuid().ToString(),
                NodeText = "ENTRYPOINT",
                IsThreatExit = false
            };

            var generatedPort = GeneratePort(node, Direction.Output);
            generatedPort.portName = "Next";
            node.outputContainer.Add(generatedPort);

            node.RefreshExpandedState();
            node.RefreshPorts();

            node.SetPosition(new Rect(100, 200, 100, 150));
            return node;
        }

        public void CreateNode(string type, Vector2 position)
        {
            AddElement(CreateInterrogationNode(type, position));
        }

        public InterrogationNode CreateInterrogationNode(string type, Vector2 position)
        {
            InterrogationNode node = null;

            switch (type)
            {
                case "Start":
                    node = new StartNode
                    {
                        title = "START",
                        GUID = System.Guid.NewGuid().ToString(),
                        NodeText = "ENTRYPOINT",
                        IsThreatExit = false
                    };
                    var sOutPort = GeneratePort(node, Direction.Output);
                    sOutPort.portName = "Next";
                    node.outputContainer.Add(sOutPort);
                    break;
                case "Dialogue":
                    node = new DialogueNode
                    {
                        title = "Dialogue",
                        GUID = Guid.NewGuid().ToString(),
                        IsThreatExit = false
                    };
                    var inputPort = GeneratePort(node, Direction.Input, Port.Capacity.Multi);
                    inputPort.portName = "Input";
                    node.inputContainer.Add(inputPort);

                    var outputPort = GeneratePort(node, Direction.Output, Port.Capacity.Single);
                    outputPort.portName = "Next";
                    node.outputContainer.Add(outputPort);

                    var dialogueField = new TextField("Dialogue:");
                    dialogueField.RegisterValueChangedCallback(evt =>
                    {
                        node.NodeText = evt.newValue;
                        ((DialogueNode)node).NodeText = evt.newValue; // Update specific field logic
                    });
                    node.mainContainer.Add(dialogueField);

                    var speakerField = new TextField("Speaker ID:");
                    speakerField.RegisterValueChangedCallback(evt =>
                    {
                        ((DialogueNode)node).SpeakerID = evt.newValue;
                    });
                    node.mainContainer.Add(speakerField);

                    break;

                case "Choice":
                    node = new ChoiceNode
                    {
                        title = "Choice",
                        GUID = Guid.NewGuid().ToString(),
                        IsThreatExit = false
                    };
                    var cInputPort = GeneratePort(node, Direction.Input, Port.Capacity.Multi);
                    cInputPort.portName = "Input";
                    node.inputContainer.Add(cInputPort);

                    var layoutButton = new Button(() => { AddChoicePort((ChoiceNode)node); });
                    layoutButton.text = "Add Choice";
                    node.titleContainer.Add(layoutButton);

                    // Silence Toggle Requirement
                    var silenceToggle = new Toggle("Has Silence Option");
                    silenceToggle.RegisterValueChangedCallback(evt =>
                    {
                        ToggleSilencePort((ChoiceNode)node, evt.newValue);
                    });
                    node.mainContainer.Add(silenceToggle);

                    break;
                
                case "End":
                    node = new EndNode
                    {
                        title = "END",
                        GUID = Guid.NewGuid().ToString(),
                        IsThreatExit = false
                    };
                    var endInputPort = GeneratePort(node, Direction.Input, Port.Capacity.Multi);
                    endInputPort.portName = "Input";
                    node.inputContainer.Add(endInputPort);
                    break;
            }
            
            // Common controls
            if (node != null && !(node is StartNode) && !(node is EndNode))
            {
                var timelineField = new TextField("Timeline Asset:");
                timelineField.RegisterValueChangedCallback(evt => node.TimelineAsset = evt.newValue);
                node.mainContainer.Add(timelineField);

                var threatToggle = new Toggle("Is Threat Exit");
                threatToggle.RegisterValueChangedCallback(evt => node.IsThreatExit = evt.newValue);
                node.mainContainer.Add(threatToggle);
            }

            if (node != null)
            {
                node.SetPosition(new Rect(position, new Vector2(150, 200)));
                node.RefreshExpandedState();
                node.RefreshPorts();
            }

            return node;
        }

        public void AddChoicePort(ChoiceNode node, string overriddenPortName = "")
        {
            var generatedPort = GeneratePort(node, Direction.Output);
            
            var oldLabel = generatedPort.contentContainer.Q<Label>("type");
            generatedPort.contentContainer.Remove(oldLabel);

            var outputPortCount = node.outputContainer.Query("connector").ToList().Count;
            var choicePortName = string.IsNullOrEmpty(overriddenPortName) ? $"Option {outputPortCount + 1}" : overriddenPortName;

            var textField = new TextField
            {
                name = string.Empty,
                value = choicePortName
            };
            textField.RegisterValueChangedCallback(evt => generatedPort.portName = evt.newValue);
            generatedPort.contentContainer.Add(new Label("  "));
            generatedPort.contentContainer.Add(textField);

            var deleteButton = new Button(() => RemovePort(node, generatedPort))
            {
                text = "X"
            };
            generatedPort.contentContainer.Add(deleteButton);

            generatedPort.portName = choicePortName;

            node.outputContainer.Add(generatedPort);
            node.RefreshPorts();
            node.RefreshExpandedState();
        }

        public void ToggleSilencePort(ChoiceNode node, bool enable)
        {
            if (enable)
            {
                var silencePort = GeneratePort(node, Direction.Output);
                silencePort.portName = "Silence";
                silencePort.name = "Silence_Port"; // Tagging it for easy find
                var label = silencePort.contentContainer.Q<Label>("type");
                if(label != null) label.text = "Silence"; // Visual indicator
                
                // Style it differently if possible (e.g., color)
                silencePort.portColor = Color.gray;

                node.outputContainer.Add(silencePort);
            }
            else
            {
                // Remove the silence port
                var ports = node.outputContainer.Query<Port>().ToList();
                foreach (var port in ports)
                {
                    if (port.portName == "Silence" && port.name == "Silence_Port")
                    {
                        // Remove connected edges explicitly?
                        // GraphView handles edge removal typically if port is removed, 
                        // but let's check
                        if (port.connected)
                        {
                            DeleteElements(port.connections);
                        }
                        node.outputContainer.Remove(port);
                        break;
                    }
                }
            }
            node.RefreshPorts();
            node.RefreshExpandedState();
        }

        private void RemovePort(Node node, Port port)
        {
            var targetEdge = port.connections;
            if (targetEdge.Count() > 0)
            {
                // Remove connections
                 DeleteElements(targetEdge);
            }
            node.outputContainer.Remove(port);
            node.RefreshPorts();
            node.RefreshExpandedState();
        }
        
        // Context Menu
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            // base.BuildContextualMenu(evt); // We can override or add to it
            var type = evt.GetType();
            Vector2 mousePosition = evt.localMousePosition;
            
            evt.menu.AppendAction("Add Dialogue Node", (a) => CreateNode("Dialogue", mousePosition));
            evt.menu.AppendAction("Add Choice Node", (a) => CreateNode("Choice", mousePosition));
            evt.menu.AppendAction("Add End Node", (a) => CreateNode("End", mousePosition));
        }
        public void ClearGraph()
        {
            var nodesToRemove = nodes.ToList().Cast<Node>().ToList();
            var edgesToRemove = edges.ToList();
            
            foreach (var node in nodesToRemove)
            {
                RemoveElement(node);
            }
            foreach (var edge in edgesToRemove)
            {
                RemoveElement(edge);
            }
        }
    }
}

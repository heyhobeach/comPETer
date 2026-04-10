using System;
using System.Collections.Generic;
using UnityEngine;

namespace Interrogation.Runtime
{
    [Serializable]
    public class InterrogationGraphData : ScriptableObject
    {
        public List<InterrogationNodeData> Nodes = new List<InterrogationNodeData>();
        public List<ConnectionData> Connections = new List<ConnectionData>();
    }

    [Serializable]
    public class InterrogationNodeData
    {
        public string ID;
        public string NodeType;
        public Vector2 Position; // For editor visualization if re-opened
        public string TimelineAsset; // The Timeline to play when this node is active
        public bool IsThreatExit;    // Requirement 1: Threat logic
    }

    [Serializable]
    public class DialogueNodeData : InterrogationNodeData
    {
        public string SpeakerID;
        public string DialogueText;
    }

    [Serializable]
    public class ChoiceNodeData : InterrogationNodeData
    {
        public List<string> Options = new List<string>();
        // Requirement 2: Silence logic is handled by a special port/option in the editor,
        // but in data, it might just be another option or a specific flag.
        // We'll trust the connection logic to handle the "Silence" routing.
    }

    [Serializable]
    public class ConnectionData
    {
        public string OutputNodeID;
        public string InputNodeID;
        public string PortName; // "Next", "Option 1", "Silence", etc.
    }
}

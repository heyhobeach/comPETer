using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEngine;

namespace Interrogation.Editor
{
    public class InterrogationNode : Node
    {
        public string GUID;
        public string NodeText;
        public bool IsThreatExit;
        public string TimelineAsset;

        public InterrogationNode()
        {
            GUID = System.Guid.NewGuid().ToString();
        }
    }

    public class DialogueNode : InterrogationNode
    {
        public string SpeakerID;
        public TextField DialogueField;
        public TextField SpeakerField;
    }

    public class ChoiceNode : InterrogationNode
    {
        public List<string> Options = new List<string>();
        // Special flag to track if silence port is enabled/connected
        public bool HasSilenceOption;
    }

    public class StartNode : InterrogationNode { }
    public class EndNode : InterrogationNode { }
}

using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Interrogation.Editor
{
    public class InterrogationGraphWindow : EditorWindow
    {
        private InterrogationGraphView _graphView;
        private string _fileName = "New Interrogation Graph";

        [MenuItem("Window/Interrogation Graph")]
        public static void OpenInterrogationGraphWindow()
        {
            var window = GetWindow<InterrogationGraphWindow>();
            window.titleContent = new GUIContent("Interrogation Graph");
        }

        private void OnEnable()
        {
            ConstructGraphView();
            GenerateToolbar();
        }

        private void ConstructGraphView()
        {
            _graphView = new InterrogationGraphView
            {
                name = "Interrogation Graph"
            };

            _graphView.StretchToParentSize();
            rootVisualElement.Add(_graphView);
        }

        private void GenerateToolbar()
        {
            var toolbar = new UnityEditor.UIElements.Toolbar();

            var fileNameTextField = new TextField("File Name:");
            fileNameTextField.SetValueWithoutNotify(_fileName);
            fileNameTextField.MarkDirtyRepaint();
            fileNameTextField.RegisterValueChangedCallback(evt => _fileName = evt.newValue);
            toolbar.Add(fileNameTextField);

            toolbar.Add(new Button(() => RequestDataOperation(true)) { text = "Save Data" });
            toolbar.Add(new Button(() => RequestDataOperation(false)) { text = "Load Data" });
            
            // CSV Operations
            toolbar.Add(new Button(() => CSVExporter.Export(_graphView)) { text = "Export to CSV" });
            toolbar.Add(new Button(() => RequestCSVImport()) { text = "Import from CSV" });

            rootVisualElement.Add(toolbar);
        }

        private void RequestCSVImport()
        {
             var saveUtility = GraphSaveUtility.GetInstance(_graphView);
             saveUtility.LoadGraphFromCSV();
        }

        private void RequestDataOperation(bool save)
        {
            if (string.IsNullOrEmpty(_fileName))
            {
                EditorUtility.DisplayDialog("Invalid File Name", "Please enter a valid file name.", "OK");
                return;
            }

            var saveUtility = GraphSaveUtility.GetInstance(_graphView);
            if (save)
            {
                saveUtility.SaveGraph(_fileName);
            }
            else
            {
                saveUtility.LoadGraph();
            }
        }

        private void OnDisable()
        {
            rootVisualElement.Remove(_graphView);
        }
    }
}

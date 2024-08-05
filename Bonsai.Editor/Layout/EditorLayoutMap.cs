using System.Collections;
using System.Collections.Generic;
using Bonsai.Expressions;

namespace Bonsai.Design
{
    internal class EditorLayoutMap : IEnumerable<BuilderLayoutSettings>
    {
        readonly TypeVisualizerMap typeVisualizerMap;
        readonly Dictionary<InspectBuilder, BuilderLayoutSettings> lookup;

        public EditorLayoutMap(TypeVisualizerMap typeVisualizers)
        {
            typeVisualizerMap = typeVisualizers;
            lookup = new();
        }

        public BuilderLayoutSettings this[InspectBuilder key]
        {
            get => lookup[key];
            set => lookup[key] = value;
        }

        public bool TryGetValue(InspectBuilder key, out BuilderLayoutSettings value)
        {
            return lookup.TryGetValue(key, out value);
        }

        private void CreateVisualizerDialogs(ExpressionBuilderGraph workflow, VisualizerDialogMap visualizerDialogs)
        {
            for (int i = 0; i < workflow.Count; i++)
            {
                var builder = (InspectBuilder)workflow[i].Value;
                if (lookup.TryGetValue(builder, out BuilderLayoutSettings builderSettings) &&
                    builderSettings.VisualizerDialogSettings != null)
                {
                    visualizerDialogs.Add(builder, workflow, builderSettings);
                }

                if (ExpressionBuilder.Unwrap(builder) is IWorkflowExpressionBuilder workflowBuilder)
                {
                    CreateVisualizerDialogs(workflowBuilder.Workflow, visualizerDialogs);
                }
            }
        }

        public VisualizerDialogMap CreateVisualizerDialogs(WorkflowBuilder workflowBuilder)
        {
            var visualizerDialogs = new VisualizerDialogMap(typeVisualizerMap);
            CreateVisualizerDialogs(workflowBuilder.Workflow, visualizerDialogs);
            return visualizerDialogs;
        }

        public void Update(IEnumerable<VisualizerDialogLauncher> visualizerDialogs)
        {
            var unused = new HashSet<InspectBuilder>(lookup.Count);
            foreach (var lookupItem in lookup)
            {
                var builderSettings = lookupItem.Value;
                if (!builderSettings.IsNestedExpanded)
                {
                    unused.Add(lookupItem.Key);
                }
            }

            foreach (var dialog in visualizerDialogs)
            {
                unused.Remove(dialog.Source);
                if (!lookup.TryGetValue(dialog.Source, out BuilderLayoutSettings builderSettings))
                {
                    builderSettings = new BuilderLayoutSettings();
                    builderSettings.Tag = dialog.Source;
                    lookup.Add(dialog.Source, builderSettings);
                }

                var visible = dialog.Visible;
                dialog.Hide();
                var dialogSettings = new VisualizerDialogSettings();
                dialogSettings.Visible = visible;
                dialogSettings.Bounds = dialog.Bounds;
                dialogSettings.WindowState = dialog.WindowState;

                var visualizer = dialog.Visualizer.Value;
                var visualizerType = visualizer.GetType();
                if (visualizerType.IsPublic)
                {
                    dialogSettings.VisualizerTypeName = visualizerType.FullName;
                    dialogSettings.VisualizerSettings = LayoutHelper.SerializeVisualizerSettings(
                        visualizer,
                        dialog.Workflow);
                }

                builderSettings.VisualizerDialogSettings = dialogSettings;
            }

            foreach (var builder in unused)
            {
                lookup.Remove(builder);
            }
        }

        public EditorLayout GetVisualizerLayout(WorkflowBuilder workflowBuilder)
        {
            return GetVisualizerLayout(workflowBuilder.Workflow);
        }

        private EditorLayout GetVisualizerLayout(ExpressionBuilderGraph workflow)
        {
            var layout = new EditorLayout();
            for (int i = 0; i < workflow.Count; i++)
            {
                var builder = (InspectBuilder)workflow[i].Value;
                var layoutSettings = new BuilderLayoutSettings { Index = i };

                if (lookup.TryGetValue(builder, out BuilderLayoutSettings builderSettings))
                {
                    layoutSettings.IsNestedExpanded = builderSettings.IsNestedExpanded;
                    layoutSettings.VisualizerDialogSettings = VisualizerDialogSettings.FromBuilderSettings(builderSettings);
                }

                if (ExpressionBuilder.Unwrap(builder) is IWorkflowExpressionBuilder workflowBuilder)
                {
                    layoutSettings.NestedLayout = GetVisualizerLayout(workflowBuilder.Workflow);
                }

                if (layoutSettings.VisualizerDialogSettings != null ||
                    layoutSettings.IsNestedExpanded ||
                    layoutSettings.NestedLayout?.BuilderSettings.Count > 0)
                {
                    layout.BuilderSettings.Add(layoutSettings);
                }
            }

            return layout;
        }

        public static EditorLayoutMap FromVisualizerLayout(
            WorkflowBuilder workflowBuilder,
            EditorLayout layout,
            TypeVisualizerMap typeVisualizers)
        {
            var visualizerSettings = new EditorLayoutMap(typeVisualizers);
            visualizerSettings.SetVisualizerLayout(workflowBuilder.Workflow, layout);
            return visualizerSettings;
        }

        public void SetVisualizerLayout(WorkflowBuilder workflowBuilder, EditorLayout layout)
        {
            Clear();
            SetVisualizerLayout(workflowBuilder.Workflow, layout);
        }

        private void SetVisualizerLayout(ExpressionBuilderGraph workflow, EditorLayout layout)
        {
            for (int i = 0; i < layout.BuilderSettings.Count; i++)
            {
                var settings = layout.BuilderSettings[i];
                var index = settings.Index.GetValueOrDefault(i);
                if (index < workflow.Count)
                {
                    var builder = (InspectBuilder)workflow[index].Value;
                    var layoutSettings = new BuilderLayoutSettings();
                    layoutSettings.Tag = builder;
                    layoutSettings.IsNestedExpanded = settings.IsNestedExpanded;
                    layoutSettings.VisualizerDialogSettings = VisualizerDialogSettings.FromBuilderSettings(settings);
                    Add(layoutSettings);

                    if (settings.NestedLayout != null &&
                        ExpressionBuilder.Unwrap(builder) is IWorkflowExpressionBuilder workflowBuilder)
                    {
                        SetVisualizerLayout(workflowBuilder.Workflow, settings.NestedLayout);
                    }
                }
            }
        }

        public void Add(BuilderLayoutSettings item)
        {
            var builder = (InspectBuilder)item.Tag;
            lookup.Add(builder, item);
        }

        public bool ContainsKey(InspectBuilder builder)
        {
            return lookup.ContainsKey(builder);
        }

        public bool Remove(InspectBuilder builder)
        {
            return lookup.Remove(builder);
        }

        public void Clear()
        {
            lookup.Clear();
        }

        public IEnumerator<BuilderLayoutSettings> GetEnumerator()
        {
            return lookup.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

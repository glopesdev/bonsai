﻿using Bonsai.Dag;
using Bonsai.Editor.GraphModel;
using Bonsai.Expressions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace Bonsai.Design
{
    static class LayoutHelper
    {
        const string MashupSettingsElement = "MashupSettings";
        const string MashupSourceElement = "Source";

        public static string GetCompatibleLayoutPath(string settingsDirectory, string fileName)
        {
            var newLayoutPath = Editor.Project.GetLayoutSettingsPath(settingsDirectory, fileName);
            return File.Exists(newLayoutPath)
                ? newLayoutPath
#pragma warning disable CS0612 // Support for deprecated layout config files
                : Editor.Project.GetLegacyLayoutSettingsPath(fileName);
#pragma warning restore CS0612 // Support for deprecated layout config files
        }

        public static void SetWorkflowNotifications(ExpressionBuilderGraph source, bool publishNotifications)
        {
            foreach (var builder in from node in source
                                    let inspectBuilder = node.Value as InspectBuilder
                                    where inspectBuilder != null
                                    select inspectBuilder)
            {
                var inspectBuilder = builder;
                inspectBuilder.PublishNotifications = publishNotifications;
                if (inspectBuilder.Builder is IWorkflowExpressionBuilder workflowExpression && workflowExpression.Workflow != null)
                {
                    SetWorkflowNotifications(workflowExpression.Workflow, publishNotifications);
                }
            }
        }

        public static void SetLayoutNotifications(ExpressionBuilderGraph source, VisualizerWindowMap lookup)
        {
            foreach (var node in source.DescendantNodes())
            {
                var inspectBuilder = (InspectBuilder)node.Value;
                if (lookup.TryGetValue(inspectBuilder, out VisualizerWindowLauncher _))
                {
                    SetVisualizerNotifications(inspectBuilder);
                }
            }
        }

        static void SetVisualizerNotifications(InspectBuilder inspectBuilder)
        {
            inspectBuilder.PublishNotifications = true;
            foreach (var visualizerMapping in ExpressionBuilder.GetVisualizerMappings(inspectBuilder))
            {
                SetVisualizerNotifications(visualizerMapping.Source);
            }
        }

        internal static Type GetMashupSourceType(Type mashupVisualizerType, Type visualizerType, TypeVisualizerMap typeVisualizerMap)
        {
            Type mashupSource = default;
            while (mashupVisualizerType != null && mashupVisualizerType != typeof(MashupVisualizer))
            {
                var mashup = typeof(MashupSource<,>).MakeGenericType(mashupVisualizerType, visualizerType);
                mashupSource = typeVisualizerMap.GetTypeVisualizers(mashup).FirstOrDefault();
                if (mashupSource != null) break;

                mashup = typeof(MashupSource<>).MakeGenericType(mashupVisualizerType);
                mashupSource = typeVisualizerMap.GetTypeVisualizers(mashup).FirstOrDefault();
                if (mashupSource != null) break;
                mashupVisualizerType = mashupVisualizerType.BaseType;
            }

            if (mashupSource != null && mashupSource.IsGenericTypeDefinition)
            {
                mashupSource = mashupSource.MakeGenericType(visualizerType);
            }
            return mashupSource;
        }

        public static VisualizerWindowLauncher CreateVisualizerLauncher(
            InspectBuilder source,
            VisualizerWindowSettings layoutSettings,
            TypeVisualizerMap typeVisualizerMap,
            ExpressionBuilderGraph workflow)
        {
            if (!source.TryGetRuntimeVisualizerSource(out InspectBuilder inspectBuilder))
            {
                throw new ArgumentException("The specified source cannot be visualized.", nameof(source));
            }

            var visualizerType = typeVisualizerMap.GetVisualizerType(layoutSettings?.VisualizerTypeName ?? string.Empty);
            visualizerType ??= typeVisualizerMap.GetTypeVisualizers(inspectBuilder).FirstOrDefault();
            if (visualizerType is null)
            {
                throw new ArgumentException("No compatible type visualizer was found.", nameof(typeVisualizerMap));
            }

            var mashupArguments = GetMashupArguments(inspectBuilder, typeVisualizerMap);
            var visualizerFactory = new VisualizerFactory(inspectBuilder, visualizerType, mashupArguments);
            var visualizer = new Lazy<DialogTypeVisualizer>(() => DeserializeVisualizerSettings(
                visualizerType,
                layoutSettings,
                workflow,
                visualizerFactory,
                typeVisualizerMap));

            var launcher = new VisualizerWindowLauncher(visualizer, visualizerFactory, workflow, source);
            launcher.Text = source != null ? ExpressionBuilder.GetElementDisplayName(source) : null;
            return launcher;
        }

        public static bool TryGetRuntimeVisualizerSource(this InspectBuilder source, out InspectBuilder inspectBuilder)
        {
            var builder = ExpressionBuilder.GetVisualizerElement(source);
            if (builder.ObservableType is not null && builder.PublishNotifications &&
                source.Builder is not VisualizerMappingBuilder)
            {
                inspectBuilder = builder;
                return true;
            }

            inspectBuilder = default;
            return false;
        }

        static IReadOnlyList<VisualizerFactory> GetMashupArguments(InspectBuilder builder, TypeVisualizerMap typeVisualizerMap)
        {
            var visualizerMappings = ExpressionBuilder.GetVisualizerMappings(builder);
            if (visualizerMappings.Count == 0) return Array.Empty<VisualizerFactory>();
            return visualizerMappings.Select(mapping =>
            {
                // stack overflow happens if a visualizer ends up being mapped to itself
                if (mapping.Source == builder)
                    throw new WorkflowBuildException("Combining together visualizer mappings from the same node is not currently supported.", builder);

                var nestedSources = GetMashupArguments(mapping.Source, typeVisualizerMap);
                var visualizerType = mapping.VisualizerType ?? typeVisualizerMap.GetTypeVisualizers(mapping.Source).FirstOrDefault();
                return new VisualizerFactory(mapping.Source, visualizerType, nestedSources);
            }).ToList();
        }

        public static XElement SerializeVisualizerSettings(
            DialogTypeVisualizer visualizer,
            IEnumerable<Node<ExpressionBuilder, ExpressionBuilderArgument>> topologicalOrder)
        {
            var visualizerType = visualizer.GetType();
            var visualizerSettings = new XDocument();
            var serializer = new XmlSerializer(visualizerType);
            using (var writer = visualizerSettings.CreateWriter())
            {
                serializer.Serialize(writer, visualizer, ElementStore.EmptyNamespaces);
            }
            var root = visualizerSettings.Root;
            if (visualizer is MashupVisualizer mashupVisualizer)
            {
                SerializeMashupVisualizerSettings(root, mashupVisualizer, topologicalOrder);
            }
            root.Remove();
            return root;
        }

        static void SerializeMashupVisualizerSettings(
            XElement root,
            MashupVisualizer mashupVisualizer,
            IEnumerable<Node<ExpressionBuilder, ExpressionBuilderArgument>> topologicalOrder)
        {
            foreach (var source in mashupVisualizer.MashupSources)
            {
                var sourceIndex = GetMashupSourceIndex(source, topologicalOrder);
                var mashupSource = SerializeMashupSource(sourceIndex, source.Visualizer, topologicalOrder);
                root.Add(mashupSource);
            }
        }

        static XElement SerializeMashupSource(
            int? sourceIndex,
            DialogTypeVisualizer visualizer,
            IEnumerable<Node<ExpressionBuilder, ExpressionBuilderArgument>> topologicalOrder)
        {
            var visualizerSettings = new XDocument();
            var visualizerType = visualizer.GetType();
            var serializer = new XmlSerializer(visualizerType);
            using (var writer = visualizerSettings.CreateWriter())
            {
                serializer.Serialize(writer, visualizer, ElementStore.EmptyNamespaces);
            }

            if (visualizer is MashupVisualizer mashupVisualizer)
            {
                SerializeMashupVisualizerSettings(visualizerSettings.Root, mashupVisualizer, topologicalOrder);
            }

            visualizerSettings = new XDocument(
                new XElement(MashupSettingsElement,
                sourceIndex.HasValue ? new XElement(MashupSourceElement, sourceIndex.Value) : null,
                new XElement(nameof(VisualizerWindowSettings.VisualizerTypeName), visualizerType.FullName),
                new XElement(nameof(VisualizerWindowSettings.VisualizerSettings), visualizerSettings.Root)));
            return visualizerSettings.Root;
        }

        public static DialogTypeVisualizer DeserializeVisualizerSettings(
            Type visualizerType,
            VisualizerWindowSettings layoutSettings,
            ExpressionBuilderGraph workflow,
            VisualizerFactory visualizerFactory,
            TypeVisualizerMap typeVisualizerMap)
        {
            if (layoutSettings?.VisualizerTypeName != visualizerType?.FullName)
            {
                layoutSettings = default;
            }

            if (layoutSettings != null && layoutSettings.Mashups.Count > 0)
            {
                var mashupSettings = layoutSettings.VisualizerSettings.Elements(MashupSettingsElement);
                foreach (var mashup in mashupSettings.Zip(layoutSettings.Mashups, (element, index) => (element, index)))
                {
                    mashup.element.AddFirst(new XElement(MashupSourceElement, mashup.index));
                    var visualizerSettings = mashup.element.Element(nameof(VisualizerWindowSettings.VisualizerSettings));
                    var visualizerTypeName = mashup.element.Element(nameof(VisualizerWindowSettings.VisualizerTypeName))?.Value;
                    if (visualizerSettings != null && visualizerTypeName != null)
                    {
                        visualizerSettings.Remove();
                        visualizerSettings.Name = visualizerTypeName.Split('.').LastOrDefault();
                        mashup.element.Add(new XElement(nameof(VisualizerWindowSettings.VisualizerSettings), visualizerSettings));
                    }
                }
                layoutSettings.Mashups.Clear();
            }

            return visualizerFactory.CreateVisualizer(layoutSettings?.VisualizerSettings, workflow, typeVisualizerMap);
        }

        static int? GetMashupSourceIndex(
            MashupSource mashup,
            IEnumerable<Node<ExpressionBuilder, ExpressionBuilderArgument>> topologicalOrder)
        {
            return topologicalOrder
                .Select((n, i) => ExpressionBuilder.GetVisualizerElement(n.Value) == mashup.Source ? (int?)i : null)
                .FirstOrDefault(index => index.HasValue);
        }

        public static DialogTypeVisualizer CreateVisualizer(
            this VisualizerFactory visualizerFactory,
            XElement visualizerSettings,
            ExpressionBuilderGraph workflow,
            TypeVisualizerMap typeVisualizerMap)
        {
            DialogTypeVisualizer visualizer;
            if (visualizerSettings != null)
            {
                var serializer = new XmlSerializer(visualizerFactory.VisualizerType);
                using var reader = visualizerSettings.CreateReader();
                visualizer = (DialogTypeVisualizer)(serializer.CanDeserialize(reader)
                    ? serializer.Deserialize(reader)
                    : Activator.CreateInstance(visualizerFactory.VisualizerType));
            }
            else visualizer = (DialogTypeVisualizer)Activator.CreateInstance(visualizerFactory.VisualizerType);

            if (visualizer is MashupVisualizer mashupVisualizer)
            {
                int index = 0;
                var mashupSettings = visualizerSettings?.Elements(MashupSettingsElement) ?? Enumerable.Empty<XElement>();
                foreach (var mashup in mashupSettings)
                {
                    VisualizerFactory mashupFactory;
                    if (index < visualizerFactory.MashupSources.Count)
                    {
                        mashupFactory = visualizerFactory.MashupSources[index++];
                    }
                    else
                    {
                        var mashupSourceElement = mashup.Element(MashupSourceElement);
                        if (mashupSourceElement == null) continue;

                        var mashupSourceIndex = int.Parse(mashupSourceElement.Value);
                        var mashupSource = (InspectBuilder)workflow[mashupSourceIndex].Value;
                        var mashupVisualizerTypeName = mashup.Element(nameof(VisualizerWindowSettings.VisualizerTypeName))?.Value;
                        var mashupVisualizerType = typeVisualizerMap.GetVisualizerType(mashupVisualizerTypeName);
                        mashupFactory = new VisualizerFactory(mashupSource, mashupVisualizerType);
                    }

                    CreateMashupVisualizer(mashupVisualizer, visualizerFactory, mashupFactory, workflow, typeVisualizerMap, mashup);
                }

                for (int i = index; i < visualizerFactory.MashupSources.Count; i++)
                {
                    var mashupFactory = visualizerFactory.MashupSources[i];
                    CreateMashupVisualizer(mashupVisualizer, visualizerFactory, mashupFactory, workflow, typeVisualizerMap);
                }
            }

            return visualizer;
        }

        static void CreateMashupVisualizer(
            MashupVisualizer mashupVisualizer,
            VisualizerFactory visualizerFactory,
            VisualizerFactory mashupFactory,
            ExpressionBuilderGraph workflow,
            TypeVisualizerMap typeVisualizerMap,
            XElement mashup = null)
        {
            var mashupSourceType = GetMashupSourceType(
                visualizerFactory.VisualizerType,
                mashupFactory.VisualizerType,
                typeVisualizerMap);
            if (mashupSourceType == null) return;
            if (mashupSourceType != typeof(DialogTypeVisualizer))
            {
                mashupFactory = new VisualizerFactory(mashupFactory.Source, mashupSourceType);
            }

            var mashupVisualizerSettings = default(XElement);
            if (mashup != null)
            {
                var mashupVisualizerSettingsElement = mashup.Element(nameof(VisualizerWindowSettings.VisualizerSettings));
                mashupVisualizerSettings = mashupVisualizerSettingsElement.Elements().FirstOrDefault();
                if (mashup.Element(nameof(VisualizerWindowSettings.VisualizerTypeName)).Value != mashupFactory.VisualizerType.FullName)
                {
                    mashupVisualizerSettings = default;
                }
            }

            var nestedVisualizer = mashupFactory.CreateVisualizer(mashupVisualizerSettings, workflow, typeVisualizerMap);
            mashupVisualizer.MashupSources.Add(mashupFactory.Source, nestedVisualizer);
        }
    }
}

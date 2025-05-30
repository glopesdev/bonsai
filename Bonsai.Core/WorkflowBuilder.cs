﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bonsai.Dag;
using System.Xml.Serialization;
using System.IO;
using Bonsai.Expressions;
using System.Reactive.Linq;
using System.Reflection;
using System.Xml.Schema;
using System.Xml;
using System.Diagnostics;
using System.Reflection.Emit;
using Bonsai.Properties;
using System.Xml.Xsl;
using System.ComponentModel;
using System.Globalization;
using Microsoft.CSharp;
using System.CodeDom;

namespace Bonsai
{
    /// <summary>
    /// Represents an XML serializable expression builder workflow container.
    /// </summary>
    public class WorkflowBuilder : IXmlSerializable
    {
        readonly ExpressionBuilderGraph workflow;
        const string DynamicAssemblyPrefix = "@Dynamic";
        const string VersionAttributeName = "Version";
        const string IncludeWorkflowTypeName = "IncludeWorkflow";
        const string TypeArgumentsAttributeName = "TypeArguments";
        const string ExtensionTypeNodeName = "ExtensionTypes";
        const string DescriptionElementName = "Description";
        const string WorkflowNodeName = "Workflow";
        const string TypeNodeName = "Type";

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowBuilder"/> class.
        /// </summary>
        public WorkflowBuilder()
            : this(new ExpressionBuilderGraph())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowBuilder"/> class with the
        /// specified workflow instance.
        /// </summary>
        /// <param name="workflow">
        /// The <see cref="ExpressionBuilderGraph"/> that will be used by this builder.
        /// </param>
        public WorkflowBuilder(ExpressionBuilderGraph workflow)
        {
            this.workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkflowBuilder"/> class from the
        /// specified workflow metadata.
        /// </summary>
        /// <param name="metadata">
        /// The <see cref="WorkflowMetadata"/> instance representing the retrieved metadata.
        /// </param>
        public WorkflowBuilder(WorkflowMetadata metadata)
            : this()
        {
            DeserializeFromMetadata(metadata);
        }

        /// <summary>
        /// Gets or sets a description for the serializable workflow.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets the <see cref="ExpressionBuilderGraph"/> instance used by this builder.
        /// </summary>
        public ExpressionBuilderGraph Workflow
        {
            get { return workflow; }
        }

        /// <summary>
        /// Gets a <see cref="XmlSerializer"/> instance that can be used to serialize
        /// or deserialize a <see cref="WorkflowBuilder"/>.
        /// </summary>
        public static XmlSerializer Serializer
        {
            get { return SerializerFactory.instance; }
        }

        /// <summary>
        /// Reads workflow metadata from the serializable XML workflow representation
        /// at the specified URI.
        /// </summary>
        /// <param name="inputUri">The URI for the file containing the XML data.</param>
        /// <returns>
        /// A <see cref="WorkflowMetadata"/> instance containing the retrieved metadata.
        /// </returns>
        public static WorkflowMetadata ReadMetadata(string inputUri)
        {
            using var reader = XmlReader.Create(inputUri);
            reader.MoveToContent();
            return ReadMetadata(reader);
        }

        /// <summary>
        /// Reads workflow metadata from the serializable XML workflow representation.
        /// </summary>
        /// <param name="reader">The <see cref="XmlReader"/> stream from which the metadata is retrieved.</param>
        /// <returns>
        /// A <see cref="WorkflowMetadata"/> instance containing the retrieved metadata.
        /// </returns>
        public static WorkflowMetadata ReadMetadata(XmlReader reader)
        {
            var visitedWorkflows = new HashSet<string>();
            return ReadMetadata(reader, visitedWorkflows);
        }

        static WorkflowMetadata ReadMetadata(XmlReader reader, HashSet<string> visitedWorkflows)
        {
            var metadata = new WorkflowMetadata();
            var serializerNamespaces = new SerializerNamespaces();
            if (reader.MoveToFirstAttribute())
            {
                do
                {
                    if (reader.Prefix != "xmlns") continue;
                    serializerNamespaces.Add(reader.LocalName, reader.Value);
                }
                while (reader.MoveToNextAttribute());
            }

            reader.ReadStartElement(typeof(WorkflowBuilder).Name);

            if (reader.IsStartElement(DescriptionElementName))
            {
                metadata.Description = reader.ReadElementContentAsString();
            }

            var types = new HashSet<Type>();
            var workflowMarkup = string.Empty;
            if (reader.IsStartElement(WorkflowNodeName))
            {
                if (reader.NamespaceURI != Constants.XmlNamespace)
                {
                    workflowMarkup = ConvertDescriptorMarkup(reader.ReadOuterXml());
                }
                else workflowMarkup = ReadXmlExtensions(reader, types, visitedWorkflows, serializerNamespaces);
            }

            if (reader.ReadToNextSibling(ExtensionTypeNodeName))
            {
                reader.ReadStartElement();
                while (reader.ReadToNextSibling(TypeNodeName))
                {
                    var typeName = reader.ReadElementString();
                    var type = LookupType(typeName);
                    var proxyTypeAttribute = (ProxyTypeAttribute)Attribute.GetCustomAttribute(type, typeof(ProxyTypeAttribute));
                    if (proxyTypeAttribute != null) type = proxyTypeAttribute.Destination;
                    types.Add(type);
                }

                if (reader.NodeType == XmlNodeType.EndElement && reader.Name == ExtensionTypeNodeName)
                {
                    reader.ReadEndElement();
                }

                types.ExceptWith(SerializerExtraTypes);
                metadata.Legacy = true;
            }

            metadata.Types = types;
            metadata.WorkflowMarkup = workflowMarkup;
            return metadata;
        }

        void DeserializeFromMetadata(WorkflowMetadata metadata)
        {
            Description = metadata.Description;
            var serializer = metadata.Legacy ? GetXmlSerializerLegacy(metadata.Types) : GetXmlSerializer(metadata.Types);
            using var workflowReader = new StringReader(metadata.WorkflowMarkup);
            var descriptor = (ExpressionBuilderGraphDescriptor)serializer.Deserialize(workflowReader);
            workflow.AddDescriptor(descriptor);
        }

        #region IXmlSerializable Members

        XmlSchema IXmlSerializable.GetSchema()
        {
            return null;
        }

        void IXmlSerializable.ReadXml(XmlReader reader)
        {
            var metadata = ReadMetadata(reader);
            DeserializeFromMetadata(metadata);
            reader.ReadEndElement();
        }

        void IXmlSerializable.WriteXml(XmlWriter writer)
        {
            var types = new HashSet<Type>();
            foreach (var type in GetExtensionTypes(workflow))
            {
                if (!type.IsPublic)
                {
                    throw new InvalidOperationException(Resources.Exception_SerializingNonPublicType);
                }

                AddExtensionType(types, type);
            }

            var serializer = GetXmlSerializer(types, out Dictionary<string, GenericTypeCode> genericTypes);
            writer = new XmlExtensionWriter(writer, genericTypes);

            var assembly = Assembly.GetExecutingAssembly();
            var version = FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion;
#if BUILD_KIND_OFFICIAL_RELEASE
            // Drop build metadata for official releases
            var plus = version.IndexOf('+');
            if (plus >= 0)
                version = version.Substring(0, plus);
#endif
            writer.WriteAttributeString(VersionAttributeName, version);

            var namespaceDeclarations = GetXmlSerializerNamespaces(types);
            foreach (var qname in namespaceDeclarations)
            {
                writer.WriteAttributeString("xmlns", qname.Name, null, qname.Namespace);
            }

            var description = Description;
            if (!string.IsNullOrEmpty(description))
            {
                writer.WriteElementString(DescriptionElementName, description);
            }

            var serializerNamespaces = new XmlSerializerNamespaces();
            serializerNamespaces.Add(string.Empty, Constants.XmlNamespace);
            serializer.Serialize(writer, workflow.ToDescriptor(), serializerNamespaces);
        }

        #endregion

        #region SerializerFactory

        static class SerializerFactory
        {
            internal static readonly XmlSerializer instance = new XmlSerializer(typeof(WorkflowBuilder), Constants.XmlNamespace);
        }

        #endregion

        #region XmlSerializer Cache

        static HashSet<Type> serializerTypes;
        static XmlSerializer serializerCache;
        static Dictionary<string, GenericTypeCode> genericTypeCache;
        static readonly CSharpCodeProvider codeProvider = new CSharpCodeProvider();
        static readonly object cacheLock = new object();
        static readonly string SystemNamespace = GetXmlNamespace(typeof(object));
        static readonly string SystemCollectionsGenericNamespace = GetXmlNamespace(typeof(IEnumerable<>));
        static readonly Type[] SerializerExtraTypes = GetDefaultSerializerTypes().ToArray();
        static readonly Type[] SerializerLegacyTypes = GetSerializerLegacyTypes().ToArray();

        static IEnumerable<Type> GetDefaultSerializerTypes()
        {
            var builderType = typeof(ExpressionBuilder);
            return builderType.Assembly.GetTypes().Where(type =>
                !type.IsGenericType &&
                (type.Namespace == builderType.Namespace || type.Namespace == nameof(Bonsai)) &&
                Attribute.IsDefined(type, typeof(XmlTypeAttribute), inherit: false) &&
                !Attribute.IsDefined(type, typeof(ObsoleteAttribute), inherit: false));
        }

        static IEnumerable<Type> GetSerializerLegacyTypes()
        {
            var builderType = typeof(ExpressionBuilder);
            return builderType.Assembly.GetTypes().Where(type =>
                !type.IsGenericType && !type.IsAbstract &&
                type.Namespace == builderType.Namespace &&
                Attribute.IsDefined(type, typeof(XmlTypeAttribute), inherit: false) &&
                Attribute.IsDefined(type, typeof(ObsoleteAttribute), inherit: false) &&
                Attribute.IsDefined(type, typeof(ProxyTypeAttribute), inherit: false))
#pragma warning disable CS0612 // Type or member is obsolete
                .Concat(new[]
                {
                    typeof(SourceBuilder),
                    typeof(CreateAsyncBuilder),
                    typeof(WindowWorkflowBuilder)
                });
#pragma warning restore CS0612 // Type or member is obsolete
        }

        static string GetXmlNamespace(Type type)
        {
            if (type.Assembly == typeof(WorkflowBuilder).Assembly &&
                type.Namespace == typeof(ExpressionBuilder).Namespace)
            {
                return Constants.XmlNamespace;
            }

            return ClrNamespace.FromType(type).ToString();
        }

        static void GetClrNamespaces(Type type, Dictionary<ClrNamespace, Assembly> clrNamespaces)
        {
            clrNamespaces[ClrNamespace.FromType(type)] = type.Assembly;
            if (type.IsGenericType)
            {
                var typeArguments = type.GetGenericArguments();
                for (int i = 0; i < typeArguments.Length; i++)
                {
                    GetClrNamespaces(typeArguments[i], clrNamespaces);
                }
            }
        }

        static SerializerNamespaces GetXmlSerializerNamespaces(HashSet<Type> types)
        {
            int namespaceIndex = 1;
            var clrNamespaces = new Dictionary<ClrNamespace, Assembly>();
            foreach (var type in types) GetClrNamespaces(type, clrNamespaces);

            var prefixCounts = new Dictionary<string, int>();
            var serializerNamespaces = new SerializerNamespaces();
            serializerNamespaces.Add("xsi", XmlSchema.InstanceNamespace);
            foreach (var item in clrNamespaces)
            {
                var clrNamespace = item.Key;
                if (clrNamespace.IsDefault) continue;

                var xmlNamespace = clrNamespace.ToString();
                if (xmlNamespace == SystemNamespace) serializerNamespaces.Add("sys", SystemNamespace);
                else if (xmlNamespace == SystemCollectionsGenericNamespace) serializerNamespaces.Add("scg", SystemCollectionsGenericNamespace);
                else
                {
                    var assembly = item.Value;
                    var prefix = (from attribute in assembly.GetCustomAttributes<XmlNamespacePrefixAttribute>()
                                  let attributeNamespace = ClrNamespace.FromUri(attribute.XmlNamespace)
                                  where attributeNamespace.Namespace == clrNamespace.Namespace
                                  select attribute.Prefix)
                                  .FirstOrDefault();
                    if (string.IsNullOrEmpty(prefix)) prefix = "p" + namespaceIndex++;
                    else
                    {
                        prefixCounts.TryGetValue(prefix, out int count);
                        prefixCounts[prefix] = ++count;
                        if (count > 1) prefix += count;
                    }
                    serializerNamespaces.Add(prefix, xmlNamespace);
                }
            }

            return serializerNamespaces;
        }

        static void AddTypeAttributeOverrides(XmlAttributeOverrides overrides, IEnumerable<Type> types)
        {
            foreach (var type in types)
            {
                var xmlTypeAttribute = (XmlTypeAttribute)type.GetCustomAttribute(typeof(XmlTypeAttribute));
                if (xmlTypeAttribute != null)
                {
                    overrides.Add(type, new XmlAttributes { XmlType = xmlTypeAttribute });
                }
            }
        }

        static XmlSerializer GetXmlSerializerLegacy(HashSet<Type> serializerTypes)
        {
            var overrides = new XmlAttributeOverrides();
            foreach (var type in serializerTypes)
            {
                var obsolete = Attribute.IsDefined(type, typeof(ObsoleteAttribute), inherit: false);
                var xmlTypeDefined = Attribute.IsDefined(type, typeof(XmlTypeAttribute), inherit: false);
                if (xmlTypeDefined && !obsolete) continue;

                var attributes = new XmlAttributes();
                if (obsolete && xmlTypeDefined)
                {
                    var xmlType = (XmlTypeAttribute)Attribute.GetCustomAttribute(type, typeof(XmlTypeAttribute));
                    attributes.XmlType = xmlType;
                }
                else attributes.XmlType = new XmlTypeAttribute { Namespace = GetXmlNamespace(type) };
                overrides.Add(type, attributes);
            }

            var extraTypes = serializerTypes.Concat(SerializerExtraTypes).Concat(SerializerLegacyTypes).ToArray();
            AddTypeAttributeOverrides(overrides, SerializerLegacyTypes);
            overrides.Add(typeof(IndexBuilder), new XmlAttributes { XmlType = new XmlTypeAttribute() { Namespace = Constants.XmlNamespace } });
            var rootAttribute = new XmlRootAttribute(WorkflowNodeName) { Namespace = Constants.XmlNamespace };
            return new XmlSerializer(typeof(ExpressionBuilderGraphDescriptor), overrides, extraTypes, rootAttribute, null);
        }

        static XmlSerializer GetXmlSerializer(HashSet<Type> types)
        {
            return GetXmlSerializer(types, out _);
        }

        static XmlSerializer GetXmlSerializer(HashSet<Type> types, out Dictionary<string, GenericTypeCode> genericTypes)
        {
            lock (cacheLock)
            {
                if (serializerCache == null || !types.IsSubsetOf(serializerTypes))
                {
                    if (serializerTypes == null) serializerTypes = types;
                    else serializerTypes.UnionWith(types);

                    genericTypeCache = new Dictionary<string, GenericTypeCode>();
                    XmlAttributeOverrides overrides = new XmlAttributeOverrides();
                    foreach (var type in serializerTypes)
                    {
                        var xmlTypeDefined = Attribute.IsDefined(type, typeof(XmlTypeAttribute), inherit: false);
                        var attributes = new XmlAttributes();
                        attributes.XmlType = xmlTypeDefined
                            ? (XmlTypeAttribute)Attribute.GetCustomAttribute(type, typeof(XmlTypeAttribute))
                            : new XmlTypeAttribute();
                        
                        if (type.IsGenericType)
                        {
                            var typeRef = new CodeTypeReference(type);
                            var typeCode = GenericTypeCode.FromType(type);
                            var genericSeparatorIndex = type.Name.LastIndexOf('`');
                            if (xmlTypeDefined && !string.IsNullOrEmpty(attributes.XmlType.TypeName))
                            {
                                typeRef.BaseType = type.Namespace + "." + attributes.XmlType.TypeName + type.Name.Substring(genericSeparatorIndex);
                                typeCode.Name = attributes.XmlType.TypeName;
                            }

                            var typeName = codeProvider.GetTypeOutput(typeRef);
                            genericTypeCache.Add(typeName, typeCode);
                            attributes.XmlType.TypeName = typeName;
                        }
                        attributes.XmlType.Namespace = GetXmlNamespace(type);
                        overrides.Add(type, attributes);
                    }

                    var extraTypes = serializerTypes.Concat(SerializerExtraTypes).Concat(SerializerLegacyTypes).ToArray();
                    AddTypeAttributeOverrides(overrides, SerializerLegacyTypes);
                    var rootAttribute = new XmlRootAttribute(WorkflowNodeName) { Namespace = Constants.XmlNamespace };
                    serializerCache = new XmlSerializer(typeof(ExpressionBuilderGraphDescriptor), overrides, extraTypes, rootAttribute, null);
                }

                genericTypes = genericTypeCache;
                return serializerCache;
            }
        }

        static IEnumerable<object> GetWorkflowElements(ExpressionBuilder builder)
        {
            yield return builder;
            var element = ExpressionBuilder.GetWorkflowElement(builder);
            if (element != builder) yield return element;

            if (element is WorkflowExpressionBuilder workflowBuilder)
            {
                foreach (var nestedElement in workflowBuilder.Workflow.SelectMany(node => GetWorkflowElements(node.Value)))
                {
                    yield return nestedElement;
                }
            }

            if (element is ISerializableElement serializableElement && (element = serializableElement.Element) != null)
            {
                yield return element;
            }
        }

        static IEnumerable<Type> GetExtensionTypes(ExpressionBuilderGraph workflow)
        {
            return workflow.SelectMany(node => GetWorkflowElements(node.Value))
                .Select(element => element.GetType())
                .Except(SerializerExtraTypes)
                .Except(SerializerLegacyTypes);
        }

        static void AddExtensionType(HashSet<Type> types, Type type)
        {
            if (types.Add(type))
            {
                // resolve any extra include types
                var xmlInclude = (XmlIncludeAttribute[])type.GetCustomAttributes(typeof(XmlIncludeAttribute), inherit: true);
                for (int i = 0; i < xmlInclude.Length; i++)
                {
                    types.Add(xmlInclude[i].Type);
                }

                while (type.BaseType != null && !SerializerExtraTypes.Contains(type.BaseType))
                {
                    type = type.BaseType;
                    if (Attribute.IsDefined(type, typeof(XmlTypeAttribute), inherit: false))
                    {
                        AddExtensionType(types, type);
                    }
                }
            }
        }

        #endregion

        #region UnknownTypeResolver

        static readonly UnknownTypeResolver TypeResolver = new UnknownTypeResolver();
        static readonly object typeResolverLock = new object();

        class UnknownTypeResolver
        {
            readonly Dictionary<string, AssemblyBuilder> dynamicAssemblies = new Dictionary<string, AssemblyBuilder>();
            readonly Dictionary<string, ModuleBuilder> dynamicModules = new Dictionary<string, ModuleBuilder>();
            readonly Dictionary<string, Type> dynamicTypes = new Dictionary<string, Type>();

            AssemblyBuilder GetDynamicAssembly(string name)
            {
                if (!dynamicAssemblies.TryGetValue(name, out AssemblyBuilder assemblyBuilder))
                {
                    var assemblyName = new AssemblyName(DynamicAssemblyPrefix + name);
                    assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
                    dynamicAssemblies.Add(name, assemblyBuilder);
                }
                return assemblyBuilder;
            }

            public Assembly ResolveAssembly(AssemblyName assemblyName)
            {
                try { return ClrNamespace.ResolveAssembly(assemblyName); }
                catch (SystemException ex)
                {
                    if (ex is IOException || ex is BadImageFormatException)
                    {
                        return GetDynamicAssembly(assemblyName.FullName);
                    }

                    throw;
                }
            }

            public Type ResolveType(Assembly assembly, string typeName, bool ignoreCase)
            {
                Type type;
                string message = Resources.Exception_UnknownTypeBuilder;
                try { type = assembly.GetType(typeName, false, ignoreCase); }
                catch (SystemException ex)
                {
                    if (ex is IOException || ex is BadImageFormatException || ex is TypeLoadException)
                    {
                        message = string.Join(" ", Resources.Exception_TypeLoadException, ex.Message);
                        type = null;
                    }
                    else throw;
                }

                if (type == null)
                {
                    var assemblyBuilder = assembly as AssemblyBuilder;
                    if (assemblyBuilder == null)
                    {
                        assemblyBuilder = GetDynamicAssembly(assembly.FullName);
                    }

                    if (!dynamicTypes.TryGetValue(typeName, out type))
                    {
                        if (!dynamicModules.TryGetValue(assembly.FullName, out ModuleBuilder moduleBuilder))
                        {
                            moduleBuilder = assemblyBuilder.DefineDynamicModule(assembly.FullName);
                            dynamicModules.Add(assembly.FullName, moduleBuilder);
                        }

                        var typeBuilder = moduleBuilder.DefineType(
                            typeName,
                            TypeAttributes.Public | TypeAttributes.Class,
                            typeof(UnknownTypeBuilder));
                        var errorMessage = string.Format(message, typeBuilder.FullName);
                        var descriptionAttributeConstructor = typeof(DescriptionAttribute).GetConstructor(new[] { typeof(string) });
                        var descriptionAttributeBuilder = new CustomAttributeBuilder(descriptionAttributeConstructor, new[] { errorMessage });
                        var obsoleteAttributeConstructor = typeof(ObsoleteAttribute).GetConstructor(Type.EmptyTypes);
                        var obsoleteAttributeBuilder = new CustomAttributeBuilder(obsoleteAttributeConstructor, new object[0]);
                        typeBuilder.SetCustomAttribute(descriptionAttributeBuilder);
                        typeBuilder.SetCustomAttribute(obsoleteAttributeBuilder);
                        type = typeBuilder.CreateTypeInfo();
                        dynamicTypes.Add(typeName, type);
                    }
                }

                return type;
            }
        }

        #endregion

        #region ConvertDescriptorMarkup

        static readonly Lazy<XslCompiledTransform> descriptorXslt = new Lazy<XslCompiledTransform>(() =>
        {
            const string XsltMarkup = @"
<xsl:stylesheet version=""1.0""
                xmlns:xsl=""http://www.w3.org/1999/XSL/Transform""
                xmlns:bonsai=""https://horizongir.org/bonsai""
                exclude-result-prefixes=""bonsai"">
  <xsl:output method=""xml"" indent=""yes""/>
  <xsl:variable name=""uri"" select=""'https://bonsai-rx.org/2018/workflow'""/>
  <xsl:template match=""@* | node()"">
    <xsl:copy>
      <xsl:apply-templates select=""@* | node()""/>
    </xsl:copy>
  </xsl:template>
  
  <xsl:template match=""bonsai:*"">
    <xsl:element name=""{local-name()}"" namespace=""{$uri}"">
      <xsl:copy-of select=""namespace::*[local-name() != '']""/>
      <xsl:apply-templates select=""@* | node()""/>
    </xsl:element>
  </xsl:template>

  <xsl:template match=""@bonsai:*"">
    <xsl:attribute name=""{local-name()}"" namespace=""{$uri}"">
      <xsl:value-of select="".""/>
    </xsl:attribute>
  </xsl:template>

  <xsl:template match=""bonsai:PropertyMappings/bonsai:Property"">
    <xsl:element name=""Property"" namespace=""{$uri}"">
      <xsl:attribute name=""Name"">
        <xsl:value-of select=""@name""/>
      </xsl:attribute>
      <xsl:if test=""@selector"">
        <xsl:attribute name=""Selector"">
          <xsl:value-of select=""@selector""/>
        </xsl:attribute>
      </xsl:if>
    </xsl:element>
  </xsl:template>

  <xsl:template match=""bonsai:Workflow/bonsai:Edges/bonsai:Edge"">
    <xsl:element name=""Edge"" namespace=""{$uri}"">
      <xsl:attribute name=""From"">
        <xsl:value-of select=""bonsai:From""/>
      </xsl:attribute>
      <xsl:attribute name=""To"">
        <xsl:value-of select=""bonsai:To""/>
      </xsl:attribute>
      <xsl:attribute name=""Label"">
        <xsl:value-of select=""bonsai:Label""/>
      </xsl:attribute>
    </xsl:element>
  </xsl:template>
</xsl:stylesheet>";
            var xslt = new XslCompiledTransform();
            using (var reader = XmlReader.Create(new StringReader(XsltMarkup)))
            {
                xslt.Load(reader);
            }
            return xslt;
        });

        static string ConvertDescriptorMarkup(string workflowMarkup)
        {
            using (var reader = new StringReader(workflowMarkup))
            using (var xmlReader = XmlReader.Create(reader))
            {
                var xslt = descriptorXslt.Value;
                using (var writer = new StringWriter())
                using (var xmlWriter = XmlWriter.Create(writer, xslt.OutputSettings))
                {
                    xslt.Transform(xmlReader, xmlWriter);
                    return writer.ToString();
                }
            }
        }

        #endregion

        #region ReadXmlExtensions

        static readonly Dictionary<string, Type> TypeForwarding = GetDefaultXmlTypeForwarding();
        static readonly Dictionary<Type, Type> ProxyTypes = GetDefaultXmlProxyTypes();

        static Dictionary<string, Type> GetDefaultXmlTypeForwarding()
        {
            var builderType = typeof(ExpressionBuilder);
            var typeMap = new Dictionary<string, Type>();
            var assemblyName = builderType.Assembly.GetName().Name;
            foreach (var type in builderType.Assembly.GetTypes().Where(type =>
                type.IsGenericType && !type.IsAbstract &&
                Attribute.IsDefined(type, typeof(XmlTypeAttribute), inherit: false) &&
                (!Attribute.IsDefined(type, typeof(ObsoleteAttribute), inherit: false) ||
                  Attribute.IsDefined(type, typeof(ProxyTypeAttribute), inherit: false))))
            {
                var xmlTypeAttribute = (XmlTypeAttribute)Attribute.GetCustomAttribute(type, typeof(XmlTypeAttribute));
                if (string.IsNullOrEmpty(xmlTypeAttribute.TypeName)) continue;
                var genericArguments = type.GetGenericArguments();
                var forwardedTypeName = type.Namespace + "." + xmlTypeAttribute.TypeName + "`" + genericArguments.Length + "," + assemblyName;
                typeMap.Add(forwardedTypeName, type);
            }
            return typeMap;
        }

        static Dictionary<Type, Type> GetDefaultXmlProxyTypes()
        {
            var builderType = typeof(ExpressionBuilder);
            var typeMap = new Dictionary<Type, Type>();
            var assemblyName = builderType.Assembly.GetName().Name;
            foreach (var type in builderType.Assembly.GetTypes().Where(type =>
                Attribute.IsDefined(type, typeof(ProxyTypeAttribute), inherit: false)))
            {
                var proxyTypeAttribute = (ProxyTypeAttribute)Attribute.GetCustomAttribute(type, typeof(ProxyTypeAttribute));
                if (proxyTypeAttribute.Destination == null) continue;
                typeMap.Add(type, proxyTypeAttribute.Destination);
            }
            return typeMap;
        }

        static string Split(string value, char separator, out string prefix)
        {
            var index = value.IndexOf(separator);
            return Split(value, index, 1, out prefix);
        }

        static string Split(string value, int index, int offset, out string prefix)
        {
            if (index >= 0)
            {
                prefix = value.Substring(0, index);
                return value.Substring(index + offset);
            }
            else
            {
                prefix = string.Empty;
                return value;
            }
        }

        struct GenericTypeToken
        {
            public string Token;
            public List<Type> TypeArguments;
        }

        static Type[] ParseTypeArguments(XmlReader reader, string value)
        {
            var i = 0;
            var builder = new StringBuilder(value.Length);
            var typeArguments = new List<Type>();
            var stack = new Stack<GenericTypeToken>();
            bool hasNext;
            do
            {
                hasNext = i < value.Length;
                var c = hasNext ? value[i++] : ',';
                switch (c)
                {
                    case '(':
                        GenericTypeToken genericType;
                        genericType.Token = builder.ToString();
                        genericType.TypeArguments = typeArguments;
                        typeArguments = new List<Type>();
                        stack.Push(genericType);
                        builder.Clear();
                        break;
                    case ',':
                    case ')':
                        if (builder.Length > 0)
                        {
                            var token = builder.ToString();
                            var type = LookupType(reader, token);
                            typeArguments.Add(type);
                            builder.Clear();
                        }

                        if (c == ')')
                        {
                            var baseType = stack.Pop();
                            var type = LookupType(reader, baseType.Token, typeArguments.ToArray());
                            typeArguments = baseType.TypeArguments;
                            typeArguments.Add(type);
                        }
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }
            while (hasNext);
            return typeArguments.ToArray();
        }

        static Type LookupType(XmlReader reader, string name, params Type[] typeArguments)
        {
            name = ResolveTypeName(reader, name, out ClrNamespace clrNamespace);
            return LookupType(name, clrNamespace, typeArguments);
        }

        static Type LookupType(string name, ClrNamespace clrNamespace, params Type[] typeArguments)
        {
            if (typeArguments != null && typeArguments.Length > 0)
            {
                name = name + '`' + typeArguments.Length;
            }

            var typeName = clrNamespace.GetAssemblyQualifiedName(name);
            return LookupType(typeName, typeArguments);
        }

        static Type LookupType(string typeName, params Type[] typeArguments)
        {
            var type = default(Type);
            try
            {
                if (!TypeForwarding.TryGetValue(typeName, out type))
                {
                    type = Type.GetType(typeName, ClrNamespace.ResolveAssembly, null, false);
                }
            }
            catch (IOException) { }
            catch (BadImageFormatException) { }
            catch (TypeLoadException) { }
            if (type == null)
            {
                lock (typeResolverLock)
                {
                    type = Type.GetType(typeName, TypeResolver.ResolveAssembly, TypeResolver.ResolveType, true);
                }
            }
            return type.IsGenericTypeDefinition ? type.MakeGenericType(typeArguments) : type;
        }

        static string ResolveTypeName(XmlReader reader, string value, out ClrNamespace clrNamespace)
        {
            var name = Split(value, ':', out string prefix);
            var ns = reader.LookupNamespace(prefix);
            if (ns == Constants.XmlNamespace) clrNamespace = ClrNamespace.Default;
            else clrNamespace = ClrNamespace.FromUri(ns);
            return name;
        }

        static Type ResolveXmlExtension(XmlReader reader, string value, string typeArguments)
        {
            var name = ResolveTypeName(reader, value, out ClrNamespace clrNamespace);
            if (clrNamespace != ClrNamespace.Default || !string.IsNullOrEmpty(typeArguments))
            {
                Type[] genericArguments = null;
                if (!string.IsNullOrEmpty(typeArguments))
                {
                    genericArguments = ParseTypeArguments(reader, typeArguments);
                }

                return LookupType(name, clrNamespace, genericArguments);
            }

            return null;
        }

        static void WriteXmlAttributes(
            XmlReader reader,
            XmlWriter writer,
            bool lookupTypes,
            HashSet<Type> types,
            HashSet<string> visitedWorkflows,
            SerializerNamespaces namespaces,
            ref bool includeWorkflow)
        {
            do
            {
                if (!reader.IsDefault && (!lookupTypes || reader.LocalName != TypeArgumentsAttributeName))
                {
                    var xsiType = string.Empty;
                    var ns = reader.NamespaceURI;
                    writer.WriteStartAttribute(reader.Prefix, reader.LocalName, ns);
                    while (reader.ReadAttributeValue())
                    {
                        if (reader.NodeType == XmlNodeType.EntityReference)
                        {
                            writer.WriteEntityRef(reader.Name);
                        }
                        else
                        {
                            var value = reader.Value;
                            if (ns == XmlSchema.InstanceNamespace)
                            {
                                xsiType = value;
                            }

                            // ensure xsi:type attributes are resolved only for workflow element types
                            if (!string.IsNullOrEmpty(xsiType) && (lookupTypes || xsiType == nameof(TypeMapping)) && !includeWorkflow)
                            {
                                // ensure xsi:type attributes nested inside include workflow properties are ignored
                                includeWorkflow = xsiType == IncludeWorkflowTypeName;
                                var typeArguments = reader.GetAttribute(TypeArgumentsAttributeName);
                                var type = ResolveXmlExtension(reader, xsiType, typeArguments);
                                if (type != null)
                                {
                                    // resolve any xsi:type proxy types
                                    if (ProxyTypes.TryGetValue(type, out Type proxyType))
                                    {
                                        var proxyNamespace = GetXmlNamespace(proxyType);
                                        var proxyPrefix = namespaces.FirstOrDefault(name => name.Namespace == proxyNamespace);
                                        if (proxyPrefix != null)
                                        {
                                            value = $"{proxyPrefix.Name}:{proxyType.Name}";
                                            type = proxyType;
                                        }
                                    }

                                    AddExtensionType(types, type);
                                    if (!string.IsNullOrEmpty(typeArguments))
                                    {
                                        var typeRef = new CodeTypeReference(type);
                                        var genericSeparatorIndex = type.Name.LastIndexOf('`');
                                        if (value.Length != genericSeparatorIndex)
                                        {
                                            // fast comparison for clipping internal type suffixes only, e.g. `1
                                            // if present, also include element namespace prefix
                                            var typeNameIndex = value.IndexOf(':') + 1;
                                            var prefix = value.Substring(0, typeNameIndex);
                                            value = value.Substring(typeNameIndex);
                                            var typeSuffix = type.Name.Substring(genericSeparatorIndex);
                                            typeRef.BaseType = prefix + type.Namespace + "." + value + typeSuffix;
                                        }

                                        var typeName = codeProvider.GetTypeOutput(typeRef);
                                        value = XmlConvert.EncodeName(typeName);
                                    }
                                }
                                else if (includeWorkflow &&
                                         reader.GetAttribute(nameof(IncludeWorkflowBuilder.Path)) is string path &&
                                         !reader.BaseURI.StartsWith(IncludeWorkflowBuilder.BuildUriPrefix) &&
                                         visitedWorkflows.Add(path))
                                {
                                    // we don't want to fail in most cases while reading nested metadata, as this
                                    // is an optional performance optimization and we would lose the visual context
                                    // as to where exactly in the workflow the failure is happening
                                    try
                                    {
                                        var embeddedResource = IncludeWorkflowBuilder.IsEmbeddedResourcePath(path);
                                        using var workflowStream = IncludeWorkflowBuilder.GetWorkflowStream(path, embeddedResource);
                                        using var workflowReader = XmlReader.Create(workflowStream, null, path);
                                        workflowReader.MoveToContent();
                                        var nestedMetadata = ReadMetadata(workflowReader, visitedWorkflows);
                                        types.UnionWith(nestedMetadata.Types);
                                    }
                                    catch (IOException) { }
                                    catch (XmlException) { }
                                    catch (BadImageFormatException) { }
                                    catch (InvalidOperationException) { }
                                    catch (UnauthorizedAccessException) { }
                                }
                            }

                            writer.WriteString(value);
                        }
                    }
                    writer.WriteEndAttribute();

                    if (!string.IsNullOrEmpty(xsiType) && includeWorkflow)
                    {
                        xsiType = Split(xsiType, ':', out ns);
                        if (!string.IsNullOrEmpty(ns) && reader.GetAttribute("xmlns" + ":" + ns) == null)
                        {
                            var qname = namespaces.FirstOrDefault(q => q.Name == ns);
                            if (qname != null)
                            {
                                writer.WriteAttributeString("xmlns", qname.Name, null, qname.Namespace);
                            }
                        }
                    }
                }
            }
            while (reader.MoveToNextAttribute());
        }

        static string ReadXmlExtensions(XmlReader reader, HashSet<Type> types, HashSet<string> visitedWorkflows, SerializerNamespaces namespaces)
        {
            const int ChunkBufferSize = 1024;
            char[] chunkBuffer = null;

            var includeDepth = -1;
            var serializerNamespaces = namespaces;
            var canReadChunk = reader.CanReadValueChunk;
            var depth = reader.NodeType == XmlNodeType.None ? -1 : reader.Depth;
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            using (var writer = XmlWriter.Create(sw))
            {
                do
                {
                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Element:
                            var elementNamespace = reader.NamespaceURI;
                            writer.WriteStartElement(reader.Prefix, reader.LocalName, elementNamespace);
                            if (namespaces != null)
                            {
                                foreach (var qname in namespaces)
                                {
                                    writer.WriteAttributeString("xmlns", qname.Name, null, qname.Namespace);
                                }
                                namespaces = null;
                            }

                            if (reader.MoveToFirstAttribute())
                            {
                                var includeWorkflow = includeDepth >= 0;
                                var lookupTypes = elementNamespace == Constants.XmlNamespace;
                                WriteXmlAttributes(reader, writer, lookupTypes, types, visitedWorkflows, serializerNamespaces, ref includeWorkflow);
                                reader.MoveToElement();
                                if (lookupTypes && includeDepth < 0 && includeWorkflow)
                                {
                                    includeDepth = reader.Depth;
                                }
                            }

                            if (reader.IsEmptyElement)
                            {
                                if (includeDepth >= 0 && reader.Depth <= includeDepth) includeDepth = -1;
                                writer.WriteEndElement();
                            }
                            break;
                        case XmlNodeType.Text:
                            if (canReadChunk)
                            {
                                int chunkSize;
                                if (chunkBuffer == null) chunkBuffer = new char[ChunkBufferSize];
                                while ((chunkSize = reader.ReadValueChunk(chunkBuffer, 0, ChunkBufferSize)) > 0)
                                {
                                    writer.WriteChars(chunkBuffer, 0, chunkSize);
                                }
                            }
                            else writer.WriteString(reader.Value);
                            break;
                        case XmlNodeType.CDATA:
                            writer.WriteCData(reader.Value);
                            break;
                        case XmlNodeType.Comment:
                            writer.WriteComment(reader.Value);
                            break;
                        case XmlNodeType.EndElement:
                            if (includeDepth >= 0 && reader.Depth <= includeDepth) includeDepth = -1;
                            writer.WriteFullEndElement();
                            break;
                        case XmlNodeType.EntityReference:
                            writer.WriteEntityRef(reader.Name);
                            break;
                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            writer.WriteWhitespace(reader.Value);
                            break;
                        case XmlNodeType.XmlDeclaration:
                        case XmlNodeType.ProcessingInstruction:
                            writer.WriteProcessingInstruction(reader.Name, reader.Value);
                            break;
                    }
                }
                while (reader.Read() && (depth < reader.Depth || (depth == reader.Depth && reader.NodeType == XmlNodeType.EndElement)));
            }
            return sw.ToString();
        }

        #endregion

        #region XmlExtensionWriter

        class GenericTypeCode
        {
            public string Name;
            public string Namespace;
            public GenericTypeCode[] TypeArguments;
            static readonly GenericTypeCode[] EmptyTypes = new GenericTypeCode[0];

            public static GenericTypeCode FromType(Type type)
            {
                var code = new GenericTypeCode();
                code.Name = type.Name;
                code.Namespace = GetXmlNamespace(type);
                if (type.IsGenericType)
                {
                    code.Name = code.Name.Substring(0, code.Name.LastIndexOf('`'));
                    code.TypeArguments = Array.ConvertAll(type.GetGenericArguments(), FromType);
                }
                else code.TypeArguments = EmptyTypes;
                return code;
            }
        }

        class SerializerNamespaces : IEnumerable<XmlQualifiedName>
        {
            readonly List<XmlQualifiedName> values = new List<XmlQualifiedName>();

            public void Add(string prefix, string ns)
            {
                values.Add(new XmlQualifiedName(prefix, ns));
            }

            public IEnumerator<XmlQualifiedName> GetEnumerator()
            {
                return values.GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        class XmlExtensionWriter : XmlWriter
        {
            int depth;
            int fragmentDepth;
            string fragmentXmlnsPrefix;
            readonly Dictionary<string, string> fragmentPrefixMap;

            bool xsiTypeAttribute;
            string xsiTypeArguments;
            readonly XmlWriter writer;
            readonly Dictionary<string, GenericTypeCode> genericTypes;

            public XmlExtensionWriter(XmlWriter writer, Dictionary<string, GenericTypeCode> genericTypes)
            {
                this.writer = writer;
                this.genericTypes = genericTypes;
                fragmentPrefixMap = new Dictionary<string, string>();
                fragmentDepth = -1;
            }

            private bool Fragment
            {
                get { return fragmentDepth >= 0; }
                set { fragmentDepth = value ? depth : -1; }
            }

            private int Depth
            {
                get { return depth; }
                set
                {
                    depth = value;
                    if (Fragment && depth < fragmentDepth)
                    {
                        Fragment = false;
                        fragmentPrefixMap.Clear();
                    }
                }
            }

            public override XmlWriterSettings Settings
            {
                get { return writer.Settings; }
            }

            public override WriteState WriteState
            {
                get { return writer.WriteState; }
            }

            public override string XmlLang
            {
                get { return writer.XmlLang; }
            }

            public override XmlSpace XmlSpace
            {
                get { return writer.XmlSpace; }
            }

            public override void Flush()
            {
                writer.Flush();
            }

            public override string LookupPrefix(string ns)
            {
                return writer.LookupPrefix(ns);
            }

            public override void WriteBase64(byte[] buffer, int index, int count)
            {
                writer.WriteBase64(buffer, index, count);
            }

            public override void WriteBinHex(byte[] buffer, int index, int count)
            {
                writer.WriteBinHex(buffer, index, count);
            }

            public override void WriteCData(string text)
            {
                writer.WriteCData(text);
            }

            public override void WriteCharEntity(char ch)
            {
                writer.WriteCharEntity(ch);
            }

            public override void WriteChars(char[] buffer, int index, int count)
            {
                writer.WriteChars(buffer, index, count);
            }

            public override void WriteComment(string text)
            {
                writer.WriteComment(text);
            }

            public override void WriteDocType(string name, string pubid, string sysid, string subset)
            {
                writer.WriteDocType(name, pubid, sysid, subset);
            }

            public override void WriteEndAttribute()
            {
                if (fragmentXmlnsPrefix != null)
                {
                    fragmentXmlnsPrefix = null;
                    return;
                }

                writer.WriteEndAttribute();
                if (xsiTypeArguments != null)
                {
                    writer.WriteAttributeString(TypeArgumentsAttributeName, xsiTypeArguments);
                    xsiTypeArguments = null;
                }
            }

            public override void WriteEndDocument()
            {
                writer.WriteEndDocument();
            }

            public override void WriteEndElement()
            {
                writer.WriteEndElement();
                Depth--;
            }

            public override void WriteEntityRef(string name)
            {
                writer.WriteEntityRef(name);
            }

            public override void WriteFullEndElement()
            {
                writer.WriteFullEndElement();
                Depth--;
            }

            public override void WriteName(string name)
            {
                writer.WriteName(name);
            }

            public override void WriteNmToken(string name)
            {
                writer.WriteNmToken(name);
            }

            public override void WriteProcessingInstruction(string name, string text)
            {
                writer.WriteProcessingInstruction(name, text);
            }

            public override void WriteQualifiedName(string localName, string ns)
            {
                writer.WriteQualifiedName(localName, ns);
            }

            public override void WriteRaw(string data)
            {
                writer.WriteRaw(data);
            }

            public override void WriteRaw(char[] buffer, int index, int count)
            {
                writer.WriteRaw(buffer, index, count);
            }

            public override void WriteStartAttribute(string prefix, string localName, string ns)
            {
                if (Fragment && prefix == "xmlns" && localName != "xsi")
                {
                    fragmentXmlnsPrefix = localName;
                    return;
                }

                xsiTypeAttribute = ns == XmlSchema.InstanceNamespace && localName == "type";
                writer.WriteStartAttribute(prefix, localName, ns);
            }

            public override void WriteStartDocument(bool standalone)
            {
                writer.WriteStartDocument(standalone);
            }

            public override void WriteStartDocument()
            {
                writer.WriteStartDocument();
            }

            public override void WriteStartElement(string prefix, string localName, string ns)
            {
                if (Fragment && !string.IsNullOrEmpty(prefix))
                {
                    prefix = writer.LookupPrefix(ns);
                }

                writer.WriteStartElement(prefix, localName, ns);
                Depth++;
            }

            string EncodeGenericType(GenericTypeCode type)
            {
                var prefix = writer.LookupPrefix(type.Namespace);
                return string.IsNullOrEmpty(prefix) ? type.Name : prefix + ":" + type.Name;
            }

            string EncodeGenericTypeArguments(GenericTypeCode[] typeArguments)
            {
                var builder = new StringBuilder();
                EncodeGenericTypeArguments(builder, typeArguments);
                return builder.ToString();
            }

            void EncodeGenericTypeArguments(StringBuilder builder, GenericTypeCode[] typeArguments)
            {
                for (int i = 0; i < typeArguments.Length; i++)
                {
                    if (i > 0) builder.Append(',');
                    var typeArgument = typeArguments[i];
                    builder.Append(EncodeGenericType(typeArgument));
                    if (typeArgument.TypeArguments.Length > 0)
                    {
                        builder.Append('(');
                        EncodeGenericTypeArguments(builder, typeArgument.TypeArguments);
                        builder.Append(')');
                    }
                }
            }

            public override void WriteString(string text)
            {
                if (fragmentXmlnsPrefix != null)
                {
                    var prefix = writer.LookupPrefix(text);
                    if (!string.IsNullOrEmpty(prefix))
                    {
                        fragmentPrefixMap[fragmentXmlnsPrefix] = prefix;
                        return;
                    }
                    else
                    {
                        writer.WriteStartAttribute("xmlns", fragmentXmlnsPrefix, null);
                        fragmentXmlnsPrefix = null;
                    }
                }

                if (writer.WriteState == WriteState.Attribute && xsiTypeAttribute)
                {
                    var name = Split(text, ':', out string typePrefix);
                    if (Fragment)
                    {
                        if (fragmentPrefixMap.TryGetValue(typePrefix, out typePrefix))
                        {
                            text = typePrefix + ":" + name;
                        }
                    }
                    else if (name == IncludeWorkflowTypeName)
                    {
                        Fragment = true;
                    }

                    var typeName = XmlConvert.DecodeName(name);
                    if (genericTypes.TryGetValue(typeName, out GenericTypeCode type))
                    {
                        text = EncodeGenericType(type);
                        if (type.TypeArguments.Length > 0)
                        {
                            xsiTypeArguments = EncodeGenericTypeArguments(type.TypeArguments);
                        }
                    }
                    xsiTypeAttribute = false;
                }

                writer.WriteString(text);
            }

            public override void WriteSurrogateCharEntity(char lowChar, char highChar)
            {
                writer.WriteSurrogateCharEntity(lowChar, highChar);
            }

            public override void WriteWhitespace(string ws)
            {
                writer.WriteWhitespace(ws);
            }
        }

        #endregion

        #region ClrNamespace

        struct ClrNamespace : IEquatable<ClrNamespace>
        {
            public readonly string Namespace;
            public readonly string AssemblyName;
            const string SchemePrefix = "clr-namespace";
            const string AssemblyNameArgument = ";assembly=";
            public static readonly ClrNamespace Default = FromType(typeof(ExpressionBuilder));
            public static readonly Dictionary<string, string> FrameworkAssemblyNames = new Dictionary<string, string>
            {
                { "mscorlib", typeof(int).Assembly.FullName },
                { "System", typeof(Uri).Assembly.FullName },
                { "System.Core", typeof(Enumerable).Assembly.FullName },
                { "System.Drawing", "System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a" }
            };

            public ClrNamespace(string ns, string assemblyName)
            {
                Namespace = ns;
                AssemblyName = assemblyName;
            }

            public bool IsDefault
            {
                get { return Equals(Default); }
            }

            internal static Assembly ResolveAssembly(AssemblyName assemblyName)
            {
                if (FrameworkAssemblyNames.TryGetValue(assemblyName.Name, out string assemblyString))
                {
                    return Assembly.Load(assemblyString);
                }
                else return Assembly.Load(assemblyName);
            }

            public static ClrNamespace FromType(Type type)
            {
                var assemblyName = type.Assembly.GetName();
#if NET462_OR_GREATER
                if (type.Assembly.GlobalAssemblyCache && !FrameworkAssemblyNames.ContainsKey(assemblyName.Name))
                {
                    return new ClrNamespace(type.Namespace, assemblyName.FullName);
                }
                else
#endif
                    return new ClrNamespace(type.Namespace, assemblyName.Name.Replace(DynamicAssemblyPrefix, string.Empty));
            }

            public static ClrNamespace FromUri(string clrNamespace)
            {
                var path = Split(clrNamespace, ':', out string prefix);
                if (prefix != SchemePrefix)
                {
                    throw new ArgumentException(Resources.Exception_InvalidTypeNamespace, "clrNamespace");
                }

                var separator = path.IndexOf(AssemblyNameArgument);
                var assemblyName = separator < 0 ? string.Empty : Split(path, separator, AssemblyNameArgument.Length, out path);
                return new ClrNamespace(path, assemblyName);
            }

            public string GetAssemblyQualifiedName(string typeName)
            {
                return string.IsNullOrEmpty(Namespace)
                    ? typeName + "," + AssemblyName
                    : Namespace + "." + typeName + "," + AssemblyName;
            }

            public override string ToString()
            {
                return SchemePrefix + ":" + Namespace + AssemblyNameArgument + AssemblyName;
            }

            public override bool Equals(object obj)
            {
                return obj is ClrNamespace ? Equals((ClrNamespace)obj) : false;
            }

            public bool Equals(ClrNamespace other)
            {
                return Namespace == other.Namespace && AssemblyName == other.AssemblyName;
            }

            public override int GetHashCode()
            {
                var hash = 53;
                if (!string.IsNullOrEmpty(Namespace)) hash = hash * 23 + Namespace.GetHashCode();
                if (!string.IsNullOrEmpty(AssemblyName)) hash = hash * 23 + AssemblyName.GetHashCode();
                return hash;
            }

            public static bool operator ==(ClrNamespace left, ClrNamespace right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(ClrNamespace left, ClrNamespace right)
            {
                return !left.Equals(right);
            }
        }

#endregion
    }
}

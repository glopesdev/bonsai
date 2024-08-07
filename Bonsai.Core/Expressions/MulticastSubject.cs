﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Xml.Serialization;

namespace Bonsai.Expressions
{
    /// <summary>
    /// Represents an expression builder that pushes a sequence of values
    /// into a shared subject.
    /// </summary>
    [DefaultProperty(nameof(Name))]
    [XmlType(Namespace = Constants.XmlNamespace)]
    [WorkflowElementCategory(ElementCategory.Sink)]
    [Description("Pushes a sequence of values into a shared subject.")]
    [TypeDescriptionProvider(typeof(MulticastSubjectTypeDescriptionProvider))]
    public class MulticastSubject : SingleArgumentExpressionBuilder, IRequireSubject
    {
        Type observableType;
        IBuildContext buildContext;

        /// <summary>
        /// Gets or sets the name of the shared subject.
        /// </summary>
        [Category("Subject")]
        [TypeConverter(typeof(SubjectNameConverter))]
        [Description("The name of the shared subject.")]
        public string Name { get; set; }

        IBuildContext IRequireBuildContext.BuildContext
        {
            get { return buildContext; }
            set { buildContext = value; }
        }

        /// <summary>
        /// Generates an <see cref="Expression"/> node from a collection of input arguments.
        /// The result can be chained with other builders in a workflow.
        /// </summary>
        /// <param name="arguments">
        /// A collection of <see cref="Expression"/> nodes that represents the input arguments.
        /// </param>
        /// <returns>An <see cref="Expression"/> tree node.</returns>
        public override Expression Build(IEnumerable<Expression> arguments)
        {
            observableType = null;
            if (buildContext == null)
            {
                throw new InvalidOperationException("No valid build context was provided.");
            }

            var name = Name;
            var source = arguments.First();
            observableType = source.Type.GetGenericArguments()[0];
            if (string.IsNullOrEmpty(name)) return source;
            var subjectExpression = buildContext.GetVariable(name);
            var subjectType = subjectExpression.Type.GetGenericArguments()[0];
            if (observableType != subjectType)
            {
                if (!HasConversion(observableType, subjectType))
                {
                    throw new InvalidOperationException(
                        $"No coercion operator is defined between types '{observableType}' and '{subjectType}'."
                    );
                }

                var conversionParameter = Expression.Parameter(observableType);
                var conversionBody = Expression.Convert(conversionParameter, subjectType);
                var conversion = Expression.Lambda(conversionBody, conversionParameter);
                return Expression.Call(
                    typeof(MulticastSubject),
                    nameof(Process),
                    new[] { observableType, subjectType },
                    source,
                    subjectExpression,
                    conversion);
            }

            return Expression.Call(typeof(MulticastSubject), nameof(Process), new[] { observableType }, source, subjectExpression);
        }

        static IObservable<TSource> Process<TSource>(IObservable<TSource> source, IObserver<TSource> subject)
        {
            return source.Do(subject);
        }

        static IObservable<TSource> Process<TSource, TSubject>(
            IObservable<TSource> source,
            IObserver<TSubject> subject,
            Func<TSource, TSubject> conversion)
        {
            return source.Do(
                value => subject.OnNext(conversion(value)),
                subject.OnError,
                subject.OnCompleted);
        }

        class MulticastSubjectTypeDescriptionProvider : TypeDescriptionProvider
        {
            readonly MulticastSubjectTypeDescriptor typeDescriptor = new MulticastSubjectTypeDescriptor(null);

            public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object instance)
            {
                return instance != null ? new MulticastSubjectTypeDescriptor(instance) : typeDescriptor;
            }
        }

        class MulticastSubjectTypeDescriptor : CustomTypeDescriptor
        {
            readonly MulticastSubject builder;
            static readonly ICustomTypeDescriptor baseDescriptor = TypeDescriptor.GetProvider(typeof(MulticastSubject))
                                                                                 .GetTypeDescriptor(typeof(MulticastSubject));

            public MulticastSubjectTypeDescriptor(object instance)
                : base(baseDescriptor)
            {
                builder = (MulticastSubject)instance;
            }

            public override PropertyDescriptorCollection GetProperties()
            {
                return GetProperties(null);
            }

            public override PropertyDescriptorCollection GetProperties(Attribute[] attributes)
            {
                var baseProperties = base.GetProperties(attributes);
                if (builder == null) return baseProperties;

                var properties = from property in baseProperties.Cast<PropertyDescriptor>()
                                 let propertyAttributes = GetPropertyAttributes(property)
                                 select new ExtendedPropertyDescriptor(property, propertyAttributes);
                return new PropertyDescriptorCollection(properties.ToArray());
            }

            Attribute[] GetPropertyAttributes(PropertyDescriptor descriptor)
            {
                var attributes = descriptor.Attributes;
                var result = new Attribute[attributes.Count + 1];
                attributes.CopyTo(result, 0);
                result[result.Length - 1] = new SubjectTargetAttribute(builder.observableType);
                return result;
            }
        }
    }

    /// <summary>
    /// This type is obsolete. Please use the <see cref="MulticastSubject"/> operator instead.
    /// </summary>
    [Obsolete]
    [ProxyType(typeof(MulticastSubject))]
    public class MulticastSubjectBuilder : MulticastSubject
    {
    }
}

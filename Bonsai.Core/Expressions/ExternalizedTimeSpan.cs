﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Xml.Serialization;

namespace Bonsai.Expressions
{
    /// <summary>
    /// Represents a <see cref="TimeSpan"/> property that has been externalized
    /// from a workflow element.
    /// </summary>
    [Obsolete]
    [XmlType(Namespace = Constants.XmlNamespace)]
    public class ExternalizedTimeSpan<TElement> : ExternalizedProperty
    {
        readonly TimeSpanProperty property = new TimeSpanProperty();

        /// <summary>
        /// Gets or sets the value of the property.
        /// </summary>
        [XmlIgnore]
        [Description("The value of the property.")]
        public TimeSpan Value
        {
            get { return property.Value; }
            set { property.Value = value; }
        }

        /// <summary>
        /// Gets or sets an XML representation of the property value for serialization.
        /// </summary>
        [Browsable(false)]
        [XmlElement(nameof(Value))]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public string ValueXml
        {
            get { return property.ValueXml; }
            set { property.ValueXml = value; }
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
            var source = arguments.FirstOrDefault();
            if (source == null)
            {
                return base.Build(arguments);
            }
            else
            {
                var propertySourceType = typeof(IObservable<TimeSpan>);
                if (source.Type != propertySourceType)
                {
                    source = ConvertExpression(source, propertySourceType);
                }

                return source;
            }
        }
    }
}

﻿using Bonsai.Expressions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Reflection;
using System.Xml.Serialization;

namespace Bonsai.Reactive
{
    /// <summary>
    /// Represents an operator that returns the elements in the observable
    /// sequence with the maximum key value.
    /// </summary>
    [DefaultProperty(nameof(KeySelector))]
    [XmlType(Namespace = Constants.XmlNamespace)]
    [Description("Returns the elements in the observable sequence with the maximum key value.")]
    public class MaxBy : SingleArgumentExpressionBuilder
    {
        static readonly MethodInfo maxBy = typeof(Observable).GetMethods()
                                                             .Single(m => m.Name == "MaxBy" &&
                                                                          m.GetParameters().Length == 2);

        /// <summary>
        /// Gets or sets a value specifying the inner properties used as key for
        /// each element in the sequence.
        /// </summary>
        [Description("Specifies the inner properties used as key for each element of the sequence.")]
        [Editor("Bonsai.Design.MultiMemberSelectorEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
        public string KeySelector { get; set; }

        /// <inheritdoc/>
        public override Expression Build(IEnumerable<Expression> arguments)
        {
            var source = arguments.First();
            var parameterType = source.Type.GetGenericArguments()[0];
            var parameter = Expression.Parameter(parameterType);
            var keySelectorBody = MemberSelector(parameter, KeySelector);
            var keySelector = Expression.Lambda(keySelectorBody, parameter);
            return Expression.Call(maxBy.MakeGenericMethod(parameterType, keySelector.ReturnType), source, keySelector);
        }
    }

    /// <summary>
    /// This type is obsolete. Please use the <see cref="MaxBy"/> operator instead.
    /// </summary>
    [Obsolete]
    [ProxyType(typeof(MaxBy))]
    [XmlType(Namespace = Constants.XmlNamespace)]
    public class MaxByBuilder : MaxBy
    {
    }
}

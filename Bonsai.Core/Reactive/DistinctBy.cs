﻿using Bonsai.Expressions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Xml.Serialization;

namespace Bonsai.Reactive
{
    /// <summary>
    /// Represents an operator that returns an observable sequence containing only
    /// elements which are distinct according to the specified key.
    /// </summary>
    [DefaultProperty(nameof(KeySelector))]
    [XmlType(Namespace = Constants.XmlNamespace)]
    [Description("Returns a sequence containing only elements which are distinct according to the specified key.")]
    public class DistinctBy : SingleArgumentExpressionBuilder
    {
        /// <summary>
        /// Gets or sets a value specifying the inner properties used to test the
        /// uniqueness of each element in the sequence.
        /// </summary>
        [Description("Specifies the inner properties used to test the uniqueness of each element in the sequence.")]
        [Editor("Bonsai.Design.MultiMemberSelectorEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
        public string KeySelector { get; set; }

        /// <inheritdoc/>
        public override Expression Build(IEnumerable<Expression> arguments)
        {
            var source = arguments.First();
            var parameterType = source.Type.GetGenericArguments()[0];
            var parameter = Expression.Parameter(parameterType);
            var keySelectorBody = MemberSelector(parameter, KeySelector);
            var keySelectorLambda = Expression.Lambda(keySelectorBody, parameter);
            var combinator = Expression.Constant(this);
            return Expression.Call(
                combinator,
                nameof(Process),
                new[] { parameter.Type, keySelectorLambda.ReturnType },
                source,
                keySelectorLambda);
        }

        IObservable<TSource> Process<TSource, TKey>(
            IObservable<TSource> source,
            Func<TSource, TKey> keySelector)
        {
            return source.Distinct(keySelector);
        }
    }

    /// <summary>
    /// This type is obsolete. Please use the <see cref="DistinctBy"/> operator instead.
    /// </summary>
    [Obsolete]
    [ProxyType(typeof(DistinctBy))]
    [XmlType(Namespace = Constants.XmlNamespace)]
    public class DistinctByBuilder : DistinctBy
    {
    }
}

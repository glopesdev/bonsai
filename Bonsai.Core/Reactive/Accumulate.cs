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
    /// Represents an operator that computes the cumulative sum of an observable sequence
    /// and returns each intermediate result.
    /// </summary>
    [XmlType(Namespace = Constants.XmlNamespace)]
    [Description("Computes the cumulative sum of an observable sequence and returns each intermediate result.")]
    public class Accumulate : SingleArgumentExpressionBuilder
    {
        static readonly MethodInfo scanMethod = typeof(Observable).GetMethods()
                                                                  .Single(m => m.Name == "Scan" &&
                                                                          m.GetParameters().Length == 2);

        /// <inheritdoc/>
        public override Expression Build(IEnumerable<Expression> arguments)
        {
            var source = arguments.Single();
            var parameterType = source.Type.GetGenericArguments()[0];
            var accumulatorParameter = Expression.Parameter(parameterType);
            var currentParameter = Expression.Parameter(parameterType);
            var accumulatorBody = Expression.Add(accumulatorParameter, currentParameter);
            var accumulator = Expression.Lambda(accumulatorBody, accumulatorParameter, currentParameter);
            return Expression.Call(scanMethod.MakeGenericMethod(parameterType), source, accumulator);
        }
    }

    /// <summary>
    /// This type is obsolete. Please use the <see cref="Accumulate"/> operator instead.
    /// </summary>
    [Obsolete]
    [ProxyType(typeof(Accumulate))]
    [XmlType(Namespace = Constants.XmlNamespace)]
    public class AccumulateBuilder : Accumulate
    {
    }
}

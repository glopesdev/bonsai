﻿using System;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Xml.Serialization;

namespace Bonsai.Reactive
{
    /// <summary>
    /// Represents an operator that returns the first element of an observable sequence,
    /// or a default value if no such element exists.
    /// </summary>
    [XmlType(Namespace = Constants.XmlNamespace)]
    [Description("Returns the first element of an observable sequence, or a default value if no such element exists.")]
    public class FirstOrDefault : Combinator
    {
        /// <summary>
        /// Returns the first element of an observable sequence, or a default value
        /// if no such element exists.
        /// </summary>
        /// <typeparam name="TSource">
        /// The type of the elements in the <paramref name="source"/> sequence.
        /// </typeparam>
        /// <param name="source">The sequence to take the first element from.</param>
        /// <returns>
        /// An observable sequence containing the first element of the
        /// <paramref name="source"/> sequence, or a default value if no
        /// such element exists.
        /// </returns>
        public override IObservable<TSource> Process<TSource>(IObservable<TSource> source)
        {
            return source.FirstOrDefaultAsync();
        }
    }
}

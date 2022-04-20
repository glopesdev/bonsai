﻿using System;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Xml.Serialization;

namespace Bonsai.Reactive
{
    /// <summary>
    /// This type is obsolete. Please use the <see cref="ElementIndex"/> operator instead.
    /// </summary>
    [Obsolete]
    [Combinator]
    [XmlType(Namespace = Constants.XmlNamespace)]
    [Description("Records the zero-based index of elements produced by an observable sequence.")]
    public class Index
    {
        /// <summary>
        /// Records the zero-based index of elements produced by an observable sequence.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
        /// <param name="source">The source sequence for which to record element indices.</param>
        /// <returns>An observable sequence with index information on elements.</returns>
        public IObservable<ElementIndex<TSource>> Process<TSource>(IObservable<TSource> source)
        {
            return source.Select((value, index) => new ElementIndex<TSource>(value, index));
        }
    }
}

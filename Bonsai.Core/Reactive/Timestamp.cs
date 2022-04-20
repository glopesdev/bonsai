﻿using System;
using System.Reactive.Linq;
using System.Xml.Serialization;
using System.ComponentModel;
using System.Reactive;

namespace Bonsai.Reactive
{
    /// <summary>
    /// Represents an operator that records the timestamp for each element produced by
    /// an observable sequence.
    /// </summary>
    [Combinator]
    [XmlType(Namespace = Constants.XmlNamespace)]
    [Description("Records the timestamp for each element produced by the sequence.")]
    public class Timestamp
    {
        /// <summary>
        /// Records the timestamp for each element produced by an observable sequence.
        /// </summary>
        /// <typeparam name="TSource">
        /// The type of the elements in the <paramref name="source"/> sequence.
        /// </typeparam>
        /// <param name="source">The source sequence to timestamp elements for.</param>
        /// <returns>An observable sequence with timestamp information on elements.</returns>
        public IObservable<Timestamped<TSource>> Process<TSource>(IObservable<TSource> source)
        {
            return source.Timestamp(HighResolutionScheduler.Default);
        }
    }
}

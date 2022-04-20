﻿using System;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Xml.Serialization;

namespace Bonsai.Reactive
{
    /// <summary>
    /// Represents an operator that repeats an observable sequence
    /// until it successfully terminates.
    /// </summary>
    [XmlType(Namespace = Constants.XmlNamespace)]
    [Description("Repeats the observable sequence until it successfully terminates.")]
    public class Retry : Combinator
    {
        /// <summary>
        /// Repeats the observable sequence until it successfully terminates.
        /// </summary>
        /// <typeparam name="TSource">
        /// The type of the elements in the <paramref name="source"/> sequence.
        /// </typeparam>
        /// <param name="source">The observable sequence to repeat until it successfully terminates.</param>
        /// <returns>
        /// The observable sequence producing the elements of the given sequence repeatedly
        /// until it terminates successfully.
        /// </returns>
        public override IObservable<TSource> Process<TSource>(IObservable<TSource> source)
        {
            return source.Retry();
        }
    }
}

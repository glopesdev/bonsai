﻿using System;
using System.Reactive.Linq;
using System.Xml.Serialization;
using System.ComponentModel;

namespace Bonsai.Reactive
{
    /// <summary>
    /// Represents an operator that ignores all elements in an observable sequence
    /// leaving only the termination messages.
    /// </summary>
    [XmlType(Namespace = Constants.XmlNamespace)]
    [Description("Ignores all elements in a sequence leaving only the termination messages.")]
    public class IgnoreElements : Combinator
    {
        /// <summary>
        /// Ignores all elements in an observable sequence leaving only the termination messages.
        /// </summary>
        /// <typeparam name="TSource">
        /// The type of the elements in the <paramref name="source"/> sequence.
        /// </typeparam>
        /// <param name="source">The source sequence.</param>
        /// <returns>
        /// An empty observable sequence that signals termination, successful or exceptional,
        /// of the source sequence.
        /// </returns>
        public override IObservable<TSource> Process<TSource>(IObservable<TSource> source)
        {
            return source.IgnoreElements();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Bonsai.Expressions
{
    /// <summary>
    /// Provides a base class for expression builders that decorate the immediately
    /// preceding builder in the workflow graph. Decorators bind backwards to a single
    /// predecessor and rewrite its compiled output. This is an abstract class.
    /// </summary>
    public abstract class DecoratorExpressionBuilder : SingleArgumentExpressionBuilder
    {
        internal DecoratorExpressionBuilder()
        {
        }

        internal ExpressionBuilder Predecessor { get; set; }

        /// <inheritdoc/>
        public sealed override Expression Build(IEnumerable<Expression> arguments)
        {
            if (Predecessor is not ExpressionBuilder predecessor)
            {
                throw new InvalidOperationException(
                    "Decorator builders require an immediately preceding builder in the workflow graph.");
            }

            Predecessor = null;

            if (predecessor is DisableBuilder)
                throw new InvalidOperationException("Cannot decorate a disabled operator.");
            if (predecessor is MulticastBranchBuilder)
                throw new InvalidOperationException("Decorators cannot be applied to a shared predecessor with more than one successor.");
            if (predecessor is PropertyMappingBuilder)
                throw new InvalidOperationException("Decorators cannot be applied to property mappings.");
            if (predecessor is VisualizerMappingExpressionBuilder)
                throw new InvalidOperationException("Decorators cannot be applied to visualizer mappings.");
            if (predecessor is DecoratorExpressionBuilder)
                throw new InvalidOperationException("Decorators cannot be chained.");

            ValidatePredecessor(predecessor);

            var source = arguments.First();
            return BuildDecorator(source, predecessor);
        }

        /// <summary>
        /// When overridden in a derived class, validates that the predecessor builder
        /// is acceptable for this decorator. The default implementation is a no-op.
        /// Universal decorator rules are applied by the base class before this method runs.
        /// </summary>
        /// <param name="predecessor">
        /// The expression builder immediately preceding this decorator in the workflow graph.
        /// </param>
        protected virtual void ValidatePredecessor(ExpressionBuilder predecessor)
        {
        }

        /// <summary>
        /// When overridden in a derived class, generates the decorator output expression
        /// given the predecessor-compiled output and the predecessor builder identity.
        /// </summary>
        /// <param name="expression">The compiled output expression of the predecessor.</param>
        /// <param name="predecessor">
        /// The expression builder immediately preceding this decorator in the workflow graph.
        /// </param>
        /// <returns>
        /// The <see cref="Expression"/> representing the rewritten output of the predecessor.
        /// </returns>
        protected abstract Expression BuildDecorator(Expression expression, ExpressionBuilder predecessor);
    }
}

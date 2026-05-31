using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Xml.Serialization;

namespace Bonsai.Expressions
{
    /// <summary>
    /// Represents a decorator that ensures the immediately preceding builder uses
    /// independent operator state on each evaluation of the enclosing expression.
    /// </summary>
    [XmlType("Isolate", Namespace = Constants.XmlNamespace)]
    [Description("Ensures the immediately preceding builder uses independent operator state on each evaluation of the enclosing expression.")]
    public class IsolateBuilder : DecoratorExpressionBuilder
    {
        static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod(
            nameof(MemberwiseClone),
            BindingFlags.Instance | BindingFlags.NonPublic);

        /// <inheritdoc/>
        protected override void ValidatePredecessor(ExpressionBuilder predecessor)
        {
            switch (predecessor)
            {
                case WorkflowInputBuilder:
                    throw new InvalidOperationException(
                        $"Cannot decorate a {nameof(WorkflowInputBuilder)}: the workflow input has no per-node state to isolate.");

                case SubjectExpressionBuilder:
                    throw new InvalidOperationException(
                        $"Cannot decorate a {nameof(SubjectExpressionBuilder)}: subject declarations have no per-node state to isolate.");

                case IRequireSubject:
                    throw new InvalidOperationException(
                        $"Cannot decorate a {predecessor.GetType().Name}: subject references have no per-node state to isolate.");
            }
        }

        /// <inheritdoc/>
        protected override Expression BuildDecorator(Expression expression, ExpressionBuilder predecessor)
        {
            var cloneCandidates = new HashSet<object>(ReferenceEqualityComparer.Instance);
            CollectCandidates(predecessor, cloneCandidates);
            if (cloneCandidates.Count == 0)
                return expression;

            if (predecessor is IWorkflowExpressionBuilder &&
                TryGetNestedScopeSelector(expression, predecessor, out var selector))
            {
                var cloneBody = BuildCloneBlock(selector.Body, cloneCandidates);
                if (cloneBody == selector.Body)
                    return expression;

                var cloneSelector = Expression.Lambda(selector.Type, cloneBody, selector.Parameters);
                return new LambdaRewriter(selector, cloneSelector).Visit(expression);
            }
            return BuildCloneBlock(expression, cloneCandidates);
        }

        static Expression BuildCloneBlock(Expression expression, HashSet<object> cloneCandidates)
        {
            var cloneStateRewriter = new CloneRewriter(cloneCandidates);
            var cloneStateExpression = cloneStateRewriter.Visit(expression);
            if (cloneStateRewriter.Locals.Count == 0)
                return expression;

            var statements = new List<Expression>(cloneStateRewriter.Assignments.Count + 1);
            statements.AddRange(cloneStateRewriter.Assignments);
            statements.Add(cloneStateExpression);
            return Expression.Block(expression.Type, cloneStateRewriter.Locals, statements);
        }

        static bool TryGetNestedScopeSelector(Expression expression, ExpressionBuilder predecessor, out LambdaExpression selector)
        {
            selector = null;
            if (InspectBuilder.UnwrapInspectableExpression(expression) is not MethodCallExpression operatorCall)
                return false;

            foreach (var argument in operatorCall.Arguments)
            {
                if (argument is LambdaExpression selectorArgument)
                {
                    if (selector != null)
                    {
                        throw new InvalidOperationException(
                            $"Cannot decorate {predecessor.GetType().Name}: Isolate only supports nested operators that " +
                            "expose a state-isolated scope as a single selector argument.");
                    }
                    selector = selectorArgument;
                }
            }
            return selector != null;
        }

        static void CollectCandidates(ExpressionBuilder builder, HashSet<object> candidates)
        {
            if (IsStatelessBuilder(builder))
                return;

            AddCandidate(builder, candidates);
            if (builder is IWorkflowExpressionBuilder workflowBuilder)
            {
                CollectFromWorkflow(workflowBuilder.Workflow, candidates);
            }
        }

        static void CollectFromWorkflow(ExpressionBuilderGraph workflow, HashSet<object> candidates)
        {
            if (workflow is null) return;
            foreach (var node in workflow)
            {
                var builder = Unwrap(node.Value);
                if (builder is DisableBuilder) continue;
                CollectCandidates(builder, candidates);
            }
        }

        static void AddCandidate(ExpressionBuilder builder, HashSet<object> candidates)
        {
            switch (builder)
            {
                case BinaryOperatorBuilder binaryOperator when binaryOperator.Operand is not null:
                    candidates.Add(binaryOperator.Operand);
                    break;

                case CombinatorBuilder combinator when combinator.Combinator is not null:
                    candidates.Add(combinator.Combinator);
                    break;

                default:
                    candidates.Add(builder);
                    break;
            }
        }

        static bool IsStatelessBuilder(ExpressionBuilder builder)
        {
            return builder is DecoratorExpressionBuilder
                or MulticastBranchBuilder
                or VisualizerMappingExpressionBuilder
                or WorkflowInputBuilder
                or WorkflowOutputBuilder
                or SubjectExpressionBuilder
                or IArgumentBuilder
                or IRequireSubject;
        }

        sealed class CloneRewriter : ExpressionVisitor
        {
            readonly Dictionary<object, ParameterExpression?> substitutions;
            readonly List<ParameterExpression> locals = new List<ParameterExpression>();
            readonly List<Expression> assignments = new List<Expression>();

            public CloneRewriter(HashSet<object> candidates)
            {
                substitutions = new Dictionary<object, ParameterExpression?>(ReferenceEqualityComparer.Instance);
                foreach (var candidate in candidates)
                {
                    substitutions[candidate] = null;
                }
            }

            public IReadOnlyList<ParameterExpression> Locals => locals;

            public IReadOnlyList<Expression> Assignments => assignments;

            protected override Expression VisitConstant(ConstantExpression node)
            {
                if (node.Value is null || !substitutions.TryGetValue(node.Value, out var local))
                    return node;

                if (local is null)
                {
                    var type = node.Value.GetType();
                    local = Expression.Parameter(type);
                    var cloneCall = Expression.Convert(
                        Expression.Call(Expression.Constant(node.Value, type), MemberwiseCloneMethod),
                        type);
                    substitutions[node.Value] = local;
                    locals.Add(local);
                    assignments.Add(Expression.Assign(local, cloneCall));
                }

                return local;
            }

            protected override Expression VisitExtension(Expression node)
            {
                return node;
            }
        }

        sealed class LambdaRewriter : ExpressionVisitor
        {
            readonly LambdaExpression target;
            readonly LambdaExpression replacement;

            public LambdaRewriter(LambdaExpression target, LambdaExpression replacement)
            {
                this.target = target;
                this.replacement = replacement;
            }

            protected override Expression VisitLambda<T>(Expression<T> node)
            {
                return ReferenceEquals(node, target) ? replacement : base.VisitLambda(node);
            }
        }

#if !NET5_0_OR_GREATER
        sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            private ReferenceEqualityComparer() { }

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
#endif
    }
}

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Xml.Serialization;

namespace Bonsai.Expressions
{
    /// <summary>
    /// Represents an operator that ensures upstream operators have independent
    /// state on each evaluation of the enclosing expression.
    /// </summary>
    [XmlType("CloneOperatorState", Namespace = Constants.XmlNamespace)]
    [Description("Ensures upstream operators have independent state on each evaluation of the enclosing expression.")]
    public class CloneOperatorStateBuilder : SingleArgumentExpressionBuilder
    {
        static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod(
            nameof(MemberwiseClone),
            BindingFlags.Instance | BindingFlags.NonPublic);

        /// <inheritdoc/>
        public override Expression Build(IEnumerable<Expression> arguments)
        {
            var source = arguments.First();

            var cloneStateRewriter = new CloneRewriter();
            var cloneStateExpression = cloneStateRewriter.Visit(source);
            if (cloneStateRewriter.Locals.Count == 0)
                return source;

            var statements = new List<Expression>(cloneStateRewriter.CloneAssignments.Count + 1);
            statements.AddRange(cloneStateRewriter.CloneAssignments);
            statements.Add(cloneStateExpression);
            return Expression.Block(source.Type, cloneStateRewriter.Locals, statements);
        }

        sealed class CloneRewriter : ExpressionVisitor
        {
            readonly Dictionary<object, ParameterExpression?> substitutions =
                new Dictionary<object, ParameterExpression?>(ReferenceEqualityComparer.Instance);

            public List<ParameterExpression> Locals { get; } = new List<ParameterExpression>();

            public List<Expression> CloneAssignments { get; } = new List<Expression>();

            protected override Expression VisitExtension(Expression node)
            {
                return node;
            }

            protected override Expression VisitConstant(ConstantExpression node)
            {
                if (node.Value is null)
                    return node;

                if (substitutions.TryGetValue(node.Value, out var local))
                    return local ?? (Expression)node;

                if (!IsCloneCandidate(node.Value))
                {
                    substitutions[node.Value] = null;
                    return node;
                }

                var type = node.Value.GetType();
                local = Expression.Parameter(type);
                var cloneCall = Expression.Convert(
                    Expression.Call(Expression.Constant(node.Value, type), MemberwiseCloneMethod),
                    type);
                CloneAssignments.Add(Expression.Assign(local, cloneCall));
                substitutions[node.Value] = local;
                Locals.Add(local);
                return local;
            }

            static bool IsCloneCandidate(object value)
            {
                if (value is InspectBuilder) return false;
                if (value is SubjectExpressionBuilder) return false;
                if (value is MulticastBranchBuilder) return false;
                if (value is ExpressionBuilder) return true;
                return value.GetType().IsDefined(typeof(CombinatorAttribute), inherit: true);
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

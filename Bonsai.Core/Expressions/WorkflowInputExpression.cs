using System;
using System.Linq.Expressions;

namespace Bonsai.Expressions
{
    class WorkflowInputExpression : Expression
    {
        public WorkflowInputExpression(Expression source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public Expression Source { get; }

        public override ExpressionType NodeType => ExpressionType.Extension;

        public override Type Type => Source.Type;

        public override bool CanReduce => true;

        public override Expression Reduce() => Source;

        public override string ToString() => Source.ToString();
    }
}

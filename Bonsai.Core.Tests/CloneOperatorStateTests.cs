using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Bonsai.Expressions;
using Bonsai.Reactive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bonsai.Core.Tests
{
    [TestClass]
    public class CloneOperatorStateTests
    {
        [Combinator]
        class ValueCollector
        {
            public int ValueCount { get; set; }

            public IObservable<int> Process()
            {
                return Observable.Return(++ValueCount);
            }

            public IObservable<int> Process<TSource>(IObservable<TSource> source)
            {
                return source.Select(_ => ++ValueCount);
            }
        }

        [Combinator]
        class ReferenceCollector
        {
            public List<int> Values { get; } = new();

            public IObservable<int> Process(IObservable<int> source)
            {
                return source.Do(Values.Add);
            }
        }

        class ConstantExpressionBuilder : ZeroArgumentExpressionBuilder
        {
            public Expression Expression { get; set; }

            public override Expression Build(IEnumerable<Expression> arguments)
            {
                return Expression;
            }
        }

        class ParameterTypeCollector : ExpressionVisitor
        {
            public HashSet<Type> Types { get; } = new();

            protected override Expression VisitParameter(ParameterExpression node)
            {
                Types.Add(node.Type);
                return node;
            }
        }


        [TestMethod]
        public async Task Build_CloneOperatorStateInSelectManyBody_IndependentInstancesPerBodyEvaluation()
        {
            var valueCollector = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendCombinator(valueCollector)
                        .Append(new CloneOperatorStateBuilder())
                        .AppendOutput(),
                    workflow => new SelectMany(workflow))
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            var lastCount = await observable.LastAsync();
            Assert.AreEqual(1, lastCount);
            Assert.AreEqual(0, valueCollector.ValueCount);
        }

        [TestMethod]
        public async Task Build_CloneOperatorStateOnOperatorWithReferenceField_SharesReferentWithOriginal()
        {
            // Documents a known limitation: MemberwiseClone is a shallow copy, so a
            // reference-typed property on the cloned operator shares its referent
            // with the original. Mutations performed through the clone remain visible
            // on the original.
            var referenceCollector = new ReferenceCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendCombinator(referenceCollector)
                        .Append(new CloneOperatorStateBuilder())
                        .AppendOutput(),
                    workflow => new SelectMany(workflow))
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            await observable.LastAsync();
            CollectionAssert.AreEqual(new[] { 0, 1 }, referenceCollector.Values);
        }

        [TestMethod]
        public async Task Build_PropertyMappingInsideCloneOperatorStateScope_TargetsCloneNotOriginal()
        {
            // PropertyMapping emits its own Expression.Constant referencing the
            // operator instance via successor.Target.Value, separate from the
            // constant emitted by CombinatorBuilder.BuildCombinator for the
            // Process call. The reference-equality dedup is what rewrites both
            // to the same clone local, so the mapped mutation lands on the
            // clone and the original property is never touched.
            var valueCollector = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendPropertyMapping(nameof(ValueCollector.ValueCount))
                        .AppendCombinator(valueCollector)
                        .Append(new CloneOperatorStateBuilder())
                        .AppendOutput(),
                    workflow => new SelectMany(workflow))
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            var lastCount = await observable.LastAsync();
            Assert.AreEqual(2, lastCount);
            Assert.AreEqual(0, valueCollector.ValueCount);
        }

        [TestMethod]
        public async Task Build_CloneOperatorStateInDeferBody_IndependentInstancesPerSubscription()
        {
            // Defer re-evaluates its inner expression on each subscription, so
            // the clone prologue fires per subscription. Independent clones
            // means the witness's last emitted value is the same on every
            // subscription, and the original property stays unchanged.
            var valueCollector = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendNested(
                    input => input
                        .AppendCombinator(new Reactive.Range { Count = 2 })
                        .AppendCombinator(valueCollector)
                        .Append(new CloneOperatorStateBuilder())
                        .AppendOutput(),
                    workflow => new Defer(workflow))
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            var first = await observable.LastAsync();
            var second = await observable.LastAsync();
            Assert.AreEqual(2, first);
            Assert.AreEqual(2, second);
            Assert.AreEqual(0, valueCollector.ValueCount);
        }

        [TestMethod]
        public async Task Build_CloneOperatorStateUpstreamOfRepeat_ClonesOnceAcrossResubscriptions()
        {
            // Repeat resubscribes to the IObservable<T> produced by a single
            // evaluation of CloneOperatorState's expression block; the body is
            // not re-evaluated. Only one clone is materialised, and its state
            // accumulates monotonically across resubscriptions (1, 2, 3, 4).
            var valueCollector = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendCombinator(valueCollector)
                .Append(new CloneOperatorStateBuilder())
                .AppendCombinator(new Reactive.Repeat())
                .AppendCombinator(new Reactive.Take { Count = 4 })
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            var last = await observable.LastAsync();
            Assert.AreEqual(4, last);
            Assert.AreEqual(0, valueCollector.ValueCount);
        }

        [TestMethod]
        public void Build_CloneOperatorStateWithNoCandidateConstants_ReturnsSourceUnchanged()
        {
            // When the upstream contains no constants matching the candidate
            // filter, CloneOperatorState takes the early-out path and returns
            // the source expression unchanged. ConstantExpressionBuilder
            // injects a raw IObservable<int> constant that the visitor rejects
            // (neither an ExpressionBuilder nor [Combinator]-attributed).
            var source = Expression.Constant(Observable.Return(0));
            var workflow = new TestWorkflow()
                .Append(new ConstantExpressionBuilder { Expression = source })
                .Append(new CloneOperatorStateBuilder())
                .AppendOutput();
            var expression = workflow.Workflow.Build();
            Assert.AreSame(source, expression);
        }

        [TestMethod]
        public void Build_CloneOperatorStateInInspectableGraph_DoesNotCloneInspectBuilders()
        {
            // InspectBuilder carries per-element inspector subjects subscribed
            // to by the visualizer pipeline. The filter excludes InspectBuilder
            // values from the candidate set, so the rewritten expression must
            // contain no ParameterExpression typed to InspectBuilder. The
            // presence of a ValueCollector parameter confirms the rewrite path
            // actually ran (guarding against a trivial early-out pass).
            var valueCollector = new ValueCollector();
            var graph = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendCombinator(valueCollector)
                        .Append(new CloneOperatorStateBuilder())
                        .AppendOutput(),
                    workflow => new SelectMany(workflow))
                .AppendOutput()
                .ToInspectableGraph();

            var expression = graph.Build();
            var collector = new ParameterTypeCollector();
            collector.Visit(expression);

            Assert.IsTrue(collector.Types.Contains(typeof(ValueCollector)),
                "Rewrite path did not emit a ValueCollector clone parameter.");
            Assert.IsFalse(collector.Types.Contains(typeof(InspectBuilder)),
                "An InspectBuilder clone parameter was emitted; the filter failed.");
        }

        [TestMethod]
        public void Build_CloneOperatorStateInWorkflowWithSubjectDeclaration_Succeeds()
        {
            // Subject builders register variables in the workflow's BuildContext.
            // CloneOperatorState's rewriter operates only on Expression.Constant
            // nodes; ParameterExpression variable references flow through
            // unchanged, so subject resolution is unaffected by cloning.
            // Probes the "subjects upstream" scenario from step 3 of the
            // prototype plan: in practice, subject scope and cloning do not
            // interact, so no explicit fail-fast detection is required.
            var valueCollector = new ValueCollector();
            var workflow = new TestWorkflow()
                .Append(new Reactive.BehaviorSubject<int> { Name = "Foo" })
                .ResetCursor()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendCombinator(valueCollector)
                .Append(new CloneOperatorStateBuilder())
                .AppendOutput();

            var expression = workflow.Workflow.Build();
            Assert.IsNotNull(expression);
        }

        [TestMethod]
        public async Task Build_CloneOperatorStateInGroupWorkflowBody_BuildsAndRunsCorrectly()
        {
            // GroupWorkflow is an open-scope operator: variables registered
            // inside its body forward to the parent BuildContext rather than
            // creating a fresh scope. Group bodies are not re-evaluated per
            // subscription, so the clone prologue fires once and the same
            // clone is reused across all upstream emissions (ValueCount goes
            // 1, 2 monotonically). Probes the open-scope scenario from step 3.
            var valueCollector = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendCombinator(valueCollector)
                        .Append(new CloneOperatorStateBuilder())
                        .AppendOutput(),
                    graph => new GroupWorkflowBuilder(graph))
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            var last = await observable.LastAsync();
            Assert.AreEqual(2, last);
            Assert.AreEqual(0, valueCollector.ValueCount);
        }

        [TestMethod]
        public void Build_SourceContainingSubjectExpressionBuilderConstant_NotCloned()
        {
            // SubjectExpressionBuilder instances resolve via the scope name
            // table during build, so they must be excluded from cloning. Tests
            // defensively against any future expression-tree shape that
            // surfaces such a constant inside a CloneOperatorState source: the
            // filter rejects it regardless. ValueCollector in the source
            // ensures the rewrite path actually runs.
            var subject = new Reactive.BehaviorSubject<int> { Name = "Test" };
            var valueCollector = new ValueCollector();
            var source = Expression.Block(
                typeof(IObservable<int>),
                Expression.Constant(subject),
                Expression.Constant(valueCollector),
                Expression.Constant(Observable.Return(0)));

            var builder = new CloneOperatorStateBuilder();
            var result = builder.Build(new[] { source });

            var collector = new ParameterTypeCollector();
            collector.Visit(result);

            Assert.IsTrue(collector.Types.Contains(typeof(ValueCollector)),
                "Rewrite path did not emit a ValueCollector clone parameter.");
            Assert.IsFalse(
                collector.Types.Any(t => typeof(SubjectExpressionBuilder).IsAssignableFrom(t)),
                "A SubjectExpressionBuilder clone parameter was emitted; the filter failed.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Bonsai.Expressions;
using Bonsai.Reactive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bonsai.Core.Tests
{
    [TestClass]
    public class IsolateBuilderTests
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

        [Combinator]
        class MergeCollector
        {
            public int ValueCount { get; set; }

            public IObservable<int> Process(IObservable<int> source1, IObservable<int> source2)
            {
                return source1.Merge(source2).Select(_ => ++ValueCount);
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

        static Exception InnermostException(Exception ex)
        {
            while (ex is WorkflowBuildException wrapped && wrapped.InnerException is not null)
                ex = wrapped.InnerException;
            return ex;
        }

        // ----- Simple combinator predecessor -----

        [TestMethod]
        public async Task Build_OnRegularCombinatorInsideSelectMany_FreshClonePerBodyEvaluation()
        {
            var valueCollector = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendCombinator(valueCollector)
                        .Append(new IsolateBuilder())
                        .AppendOutput(),
                    workflow => new SelectMany(workflow))
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            var lastCount = await observable.LastAsync();
            Assert.AreEqual(1, lastCount);
            Assert.AreEqual(0, valueCollector.ValueCount);
        }

        [TestMethod]
        public async Task Build_OnCombinatorWithReferenceField_SharesReferentWithOriginal()
        {
            // Documents a known limitation: the decorator performs a shallow
            // clone, so reference-typed properties on the cloned operator share
            // their referent with the original. Mutations through the clone
            // remain visible on the original.
            var referenceCollector = new ReferenceCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendCombinator(referenceCollector)
                        .Append(new IsolateBuilder())
                        .AppendOutput(),
                    workflow => new SelectMany(workflow))
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            await observable.LastAsync();
            CollectionAssert.AreEqual(new[] { 0, 1 }, referenceCollector.Values);
        }

        [TestMethod]
        public async Task Build_PropertyMappingUpstreamOfPredecessor_TargetsCloneNotOriginal()
        {
            // A PropertyMapping upstream of the decorator writes to a property on
            // the predecessor. The write should target the clone, not the
            // original; the clone observes the mapped value while the original
            // property stays untouched.
            var valueCollector = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendPropertyMapping(nameof(ValueCollector.ValueCount))
                        .AppendCombinator(valueCollector)
                        .Append(new IsolateBuilder())
                        .AppendOutput(),
                    workflow => new SelectMany(workflow))
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            var lastCount = await observable.LastAsync();
            Assert.AreEqual(2, lastCount);
            Assert.AreEqual(0, valueCollector.ValueCount);
        }

        [TestMethod]
        public async Task Build_OnCombinatorInsideDeferBody_FreshClonePerSubscription()
        {
            // Decorating a combinator inside a Defer body produces a fresh clone
            // per subscription. Both subscriptions see the same last emitted
            // value because each starts from a fresh clone.
            var valueCollector = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendNested(
                    input => input
                        .AppendCombinator(new Reactive.Range { Count = 2 })
                        .AppendCombinator(valueCollector)
                        .Append(new IsolateBuilder())
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
        public async Task Build_UpstreamOfRepeat_ClonesOnceAcrossResubscriptions()
        {
            // RepeatCount resubscribes to its source without re-evaluating it.
            // The clone is therefore created once and accumulates state across
            // resubscriptions. Range(2) emits two values per subscription, so
            // two resubscriptions yield values 1, 2 then 3, 4, ending at 4.
            var valueCollector = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendCombinator(valueCollector)
                .Append(new IsolateBuilder())
                .AppendCombinator(new RepeatCount { Count = 2 })
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            var last = await observable.LastAsync();
            Assert.AreEqual(4, last);
            Assert.AreEqual(0, valueCollector.ValueCount);
        }

        [TestMethod]
        public void Build_OnPredecessorWithNoSelfReferencingConstants_ReturnsSourceUnchanged()
        {
            // When the compiled output of the predecessor does not reference the
            // operator instance, the decorator has nothing to clone and returns
            // the source expression unchanged, reference-equal to the input.
            var source = Expression.Constant(Observable.Return(0));
            var workflow = new TestWorkflow()
                .Append(new ConstantExpressionBuilder { Expression = source })
                .Append(new IsolateBuilder())
                .AppendOutput();
            var expression = workflow.Workflow.Build();
            Assert.AreSame(source, expression);
        }

        [TestMethod]
        public void Build_InInspectableGraph_DoesNotCloneInspectBuilders()
        {
            // When the workflow is built as inspectable, the decorator should
            // clone only the operator immediately upstream of it, not the
            // inspector wrappers added around every node by the inspectable build.
            var valueCollector = new ValueCollector();
            var graph = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendCombinator(valueCollector)
                        .Append(new IsolateBuilder())
                        .AppendOutput(),
                    workflow => new SelectMany(workflow))
                .AppendOutput()
                .ToInspectableGraph();

            var expression = graph.Build();
            var collector = new ParameterTypeCollector();
            collector.Visit(expression);

            Assert.IsTrue(collector.Types.Contains(typeof(ValueCollector)),
                "The decorator did not produce a ValueCollector clone parameter.");
            Assert.IsFalse(collector.Types.Contains(typeof(InspectBuilder)),
                "An InspectBuilder clone parameter was produced; inspector wrappers should not be cloned.");
        }

        [TestMethod]
        public void Build_InWorkflowWithSubjectDeclaration_Succeeds()
        {
            // A subject declared in the workflow should not interfere with
            // cloning elsewhere in the workflow. The build succeeds.
            var valueCollector = new ValueCollector();
            var workflow = new TestWorkflow()
                .Append(new BehaviorSubject<int> { Name = "Foo" })
                .ResetCursor()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendCombinator(valueCollector)
                .Append(new IsolateBuilder())
                .AppendOutput();

            var expression = workflow.Workflow.Build();
            Assert.IsNotNull(expression);
        }

        // ----- Nested-scope predecessor -----

        [TestMethod]
        public async Task Build_OnDeferPredecessor_FreshClonePerSubscription()
        {
            // Decorating a Defer from outside should clone every operator inside
            // the Defer body, with a fresh clone produced per subscription. Defer
            // re-evaluates its body on each subscription. Accumulating state
            // across subscriptions indicates the clone is shared instead of being
            // produced per subscription.
            var inner = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendNested(
                    input => input
                        .AppendCombinator(new Reactive.Range { Count = 2 })
                        .AppendCombinator(inner)
                        .AppendOutput(),
                    workflow => new Defer(workflow))
                .Append(new IsolateBuilder())
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            var first = await observable.LastAsync();
            var second = await observable.LastAsync();
            Assert.AreEqual(2, first);
            Assert.AreEqual(2, second,
                "Second subscription saw accumulated state; the clone is shared across subscriptions instead of being produced per subscription.");
            Assert.AreEqual(0, inner.ValueCount,
                "ValueCollector inside Defer was not cloned: original mutated.");
        }

        [TestMethod]
        public async Task Build_OnSelectManyPredecessor_FreshClonePerNotification()
        {
            // Decorating a SelectMany from outside should clone every operator
            // inside the SelectMany body, with a fresh clone produced per source
            // notification. ValueCollector returns ++ValueCount per call; fresh
            // clones yield last=1 across two notifications, while a shared clone
            // would accumulate to last=2.
            var inner = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendCombinator(inner)
                        .AppendOutput(),
                    workflow => new SelectMany(workflow))
                .Append(new IsolateBuilder())
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            var last = await observable.LastAsync();
            Assert.AreEqual(1, last,
                "Last emitted value indicates accumulated ValueCount across SelectMany notifications; the clone is shared instead of being produced per notification.");
            Assert.AreEqual(0, inner.ValueCount,
                "ValueCollector inside SelectMany was not cloned: original mutated.");
        }

        [TestMethod]
        public async Task Build_OnGroupWorkflowPredecessor_ClonesAllOperatorsInsideScope()
        {
            // Decorating a GroupWorkflow from outside should clone every operator
            // inside the group body. The group is inlined and has no re-evaluation
            // discipline of its own, so the clone block runs once per outer
            // evaluation. With the group at top level this means one clone for the
            // whole subscription: the inner ValueCollector counts both values of
            // the Range on the clone, and the original instance is untouched.
            var inner = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendCombinator(inner)
                        .AppendOutput(),
                    graph => new GroupWorkflowBuilder(graph))
                .Append(new IsolateBuilder())
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            var last = await observable.LastAsync();
            Assert.AreEqual(2, last);
            Assert.AreEqual(0, inner.ValueCount,
                "ValueCollector inside GroupWorkflow was not cloned: original mutated.");
        }

        [TestMethod]
        public async Task Build_OnGroupWorkflowPredecessorInsideSelectMany_FreshClonePerNotification()
        {
            // A GroupWorkflow inside a SelectMany re-inlines its body per source
            // notification. The clone block placed by the outer decorator
            // therefore runs once per notification, and each notification observes
            // a fresh ValueCollector with ValueCount=0. Last emitted value is 1
            // rather than accumulating to 2.
            var inner = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendNested(
                            groupInput => groupInput
                                .AppendCombinator(inner)
                                .AppendOutput(),
                            graph => new GroupWorkflowBuilder(graph))
                        .Append(new IsolateBuilder())
                        .AppendOutput(),
                    graph => new SelectMany(graph))
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            var last = await observable.LastAsync();
            Assert.AreEqual(1, last,
                "Last emitted value indicates accumulated ValueCount across SelectMany notifications; the group clone is shared instead of being produced per notification.");
            Assert.AreEqual(0, inner.ValueCount,
                "ValueCollector inside GroupWorkflow was not cloned: original mutated.");
        }

        [TestMethod]
        public async Task Build_InsideGroupWorkflowBody_FreshClonePerSubscription()
        {
            // The decorator placed inside the body of a GroupWorkflow, immediately after a
            // combinator, clones only that combinator. The group itself is transparent;
            // operators outside the group are untouched. This confirms locality at the
            // application layer (predecessor = the inner ValueCollector).
            var valueCollector = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendCombinator(valueCollector)
                        .Append(new IsolateBuilder())
                        .AppendOutput(),
                    graph => new GroupWorkflowBuilder(graph))
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            var last = await observable.LastAsync();
            Assert.AreEqual(2, last);
            Assert.AreEqual(0, valueCollector.ValueCount);
        }

        [TestMethod]
        public async Task Build_OnGroupWorkflowBody_DoesNotReachOutsideGroup()
        {
            // The decorator placed at the end of the group interior decorates only the
            // inner ValueCollector. The parent ValueCollector upstream of the group sits
            // outside the decorator scope and runs on the original instance.
            var parentCollector = new ValueCollector();
            var innerCollector = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendCombinator(parentCollector)
                .AppendNested(
                    input => input
                        .AppendCombinator(innerCollector)
                        .Append(new IsolateBuilder())
                        .AppendOutput(),
                    graph => new GroupWorkflowBuilder(graph))
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            await observable.LastAsync();
            Assert.AreEqual(2, parentCollector.ValueCount,
                "Parent operator was cloned; the decorator should not reach outside the local predecessor.");
            Assert.AreEqual(0, innerCollector.ValueCount,
                "Inner operator was not cloned; the decorator regressed on the within-group case.");
        }

        [TestMethod]
        public async Task Build_NestedDecoratorAcrossNestedOperatorBoundary_CloneOfClones()
        {
            // Two Isolate decorators stacked: the outer decorates a SelectMany,
            // and the inner sits inside the SelectMany body decorating ValueCollector.
            // The original instance is never reached, and each notification observes
            // ValueCount=1 because the inner clone is always freshly cloned from a
            // freshly cloned outer clone.
            var inner = new ValueCollector();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendCombinator(inner)
                        .Append(new IsolateBuilder())
                        .AppendOutput(),
                    workflow => new SelectMany(workflow))
                .Append(new IsolateBuilder())
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            var last = await observable.LastAsync();
            Assert.AreEqual(1, last,
                "Last emitted value indicates the clone-of-clones chain did not produce a fresh inner clone per notification.");
            Assert.AreEqual(0, inner.ValueCount,
                "Original ValueCollector was mutated; the outer or inner clone layer failed to protect the original.");
        }

        [TestMethod]
        public void Build_OnInspectableSelectManyPredecessor_RewritesInsideSelector()
        {
            // Companion to Build_OnSelectManyPredecessor_FreshClonePerNotification
            // under an inspectable build. The decorator should still clone the
            // inner ValueCollector when the workflow is wrapped with inspector
            // instrumentation.
            var inner = new ValueCollector();
            var graph = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendCombinator(inner)
                        .AppendOutput(),
                    workflow => new SelectMany(workflow))
                .Append(new IsolateBuilder())
                .AppendOutput()
                .ToInspectableGraph();

            var expression = graph.Build();
            var collector = new ParameterTypeCollector();
            collector.Visit(expression);

            Assert.IsTrue(collector.Types.Contains(typeof(ValueCollector)),
                "Expected a ValueCollector clone parameter under an inspectable build; the decorator did not clone the inner operator.");
        }

        // ----- Branch and join semantics -----

        [TestMethod]
        public async Task Build_AtMulticastJoin_ClonesJoinOperatorOnly()
        {
            // The decorator is placed after a merge that closes a multicast scope. Its
            // predecessor is the merge combinator only; branch operators are upstream of
            // the join and not in the decorator scope. mergeCollector is cloned;
            // parent/branch collectors run on their originals.
            var parentCollector = new ValueCollector();
            var branchACollector = new ValueCollector();
            var branchBCollector = new ValueCollector();
            var mergeCollector = new MergeCollector();

            var rangeAndParent = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendCombinator(parentCollector);
            var branchA = rangeAndParent.AppendCombinator(branchACollector);
            var branchB = rangeAndParent.AppendCombinator(branchBCollector);
            var workflow = branchA
                .AppendCombinator(mergeCollector)
                .AddArguments(branchB)
                .Append(new IsolateBuilder())
                .AppendOutput();

            var observable = workflow.BuildObservable<int>();
            await observable.LastAsync();
            Assert.AreEqual(2, parentCollector.ValueCount,
                "Parent collector should run on the original; only the merge is cloned.");
            Assert.AreEqual(2, branchACollector.ValueCount,
                "Branch A collector should run on the original; only the merge is cloned.");
            Assert.AreEqual(2, branchBCollector.ValueCount,
                "Branch B collector should run on the original; only the merge is cloned.");
            Assert.AreEqual(0, mergeCollector.ValueCount,
                "Merge operator was not cloned even though the decorator was placed immediately after it.");
        }

        [TestMethod]
        public async Task Build_InsideOpenMulticastBranch_ClonesOnlyLocalOperator()
        {
            // Each decorator inside a dangling branch clones only its own
            // immediate predecessor. Cloning does not propagate across multicast
            // branches.
            var parentCollector = new ValueCollector();
            var branchACollector = new ValueCollector();
            var branchBCollector = new ValueCollector();

            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendCombinator(parentCollector)
                .AppendBranch(source => source
                    .AppendCombinator(branchACollector)
                    .Append(new IsolateBuilder())
                    .ResetCursor(source.Cursor)
                    .AppendCombinator(branchBCollector)
                    .Append(new IsolateBuilder()));

            var observable = workflow.BuildObservable<Unit>();
            await observable.LastOrDefaultAsync();
            Assert.AreEqual(2, parentCollector.ValueCount,
                "Parent operator was cloned across the multicast boundary; the decorator should not have reached past the local predecessor.");
            Assert.AreEqual(0, branchACollector.ValueCount,
                "Branch A operator was not cloned by its in-branch decorator.");
            Assert.AreEqual(0, branchBCollector.ValueCount,
                "Branch B operator was not cloned by its in-branch decorator.");
        }

        // ----- Application throw cases -----

        [TestMethod]
        public void Build_OnWorkflowInputPredecessor_Throws()
        {
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .Append(new IsolateBuilder())
                        .AppendOutput(),
                    graph => new SelectMany(graph))
                .AppendOutput();

            var ex = Assert.ThrowsExactly<WorkflowBuildException>(() => workflow.Workflow.Build());
            Assert.IsInstanceOfType(InnermostException(ex), typeof(InvalidOperationException));
        }

        [TestMethod]
        public void Build_OnSubjectExpressionBuilderPredecessor_Throws()
        {
            var workflow = new TestWorkflow()
                .AppendSubject<BehaviorSubject<int>>("Foo")
                .Append(new IsolateBuilder())
                .AppendOutput();

            var ex = Assert.ThrowsExactly<WorkflowBuildException>(() => workflow.Workflow.Build());
            Assert.IsInstanceOfType(InnermostException(ex), typeof(InvalidOperationException));
        }

        [TestMethod]
        public void Build_OnSubjectReferencePredecessor_Throws()
        {
            var workflow = new TestWorkflow()
                .AppendSubject<BehaviorSubject<int>>("Foo")
                .ResetCursor()
                .Append(new SubscribeSubject { Name = "Foo" })
                .Append(new IsolateBuilder())
                .AppendOutput();

            var ex = Assert.ThrowsExactly<WorkflowBuildException>(() => workflow.Workflow.Build());
            Assert.IsInstanceOfType(InnermostException(ex), typeof(InvalidOperationException));
        }

        [TestMethod]
        public void Build_OnChainedDecoratorPredecessor_Throws()
        {
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendCombinator(new ValueCollector())
                .Append(new IsolateBuilder())
                .Append(new IsolateBuilder())
                .AppendOutput();

            var ex = Assert.ThrowsExactly<WorkflowBuildException>(() => workflow.Workflow.Build());
            Assert.IsInstanceOfType(InnermostException(ex), typeof(InvalidOperationException));
        }

        [TestMethod]
        public void Build_OnDisabledPredecessor_Throws()
        {
            // A disabled operator is inert at runtime, so cloning its state would
            // have no observable effect. The decorator throws to surface the
            // misplacement rather than silently doing nothing.
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .Append(new DisableBuilder(new CombinatorBuilder { Combinator = new ValueCollector() }))
                .Append(new IsolateBuilder())
                .AppendOutput();

            var ex = Assert.ThrowsExactly<WorkflowBuildException>(() => workflow.Workflow.Build());
            Assert.IsInstanceOfType(InnermostException(ex), typeof(InvalidOperationException));
        }

        [TestMethod]
        public void Build_OnSharedPredecessor_Throws()
        {
            // When the predecessor has more than one successor, cloning becomes
            // ambiguous: the clone could apply to this branch only or to all
            // branches. The decorator throws to keep the scope unambiguous.
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendCombinator(new ValueCollector())
                .AppendBranch(source => source
                    .Append(new IsolateBuilder())
                    .ResetCursor(source.Cursor)
                    .AppendCombinator(new ValueCollector()));

            var ex = Assert.ThrowsExactly<WorkflowBuildException>(() => workflow.Workflow.Build());
            Assert.IsInstanceOfType(InnermostException(ex), typeof(InvalidOperationException));
        }

        [TestMethod]
        public void Build_OnInputMappingPredecessor_Throws()
        {
            // An InputMapping is a property mapping rather than an operator with
            // its own state. Placing the decorator immediately after an
            // InputMapping throws.
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .Append(new InputMappingBuilder())
                .Append(new IsolateBuilder())
                .AppendOutput();

            var ex = Assert.ThrowsExactly<WorkflowBuildException>(() => workflow.Workflow.Build());
            Assert.IsInstanceOfType(InnermostException(ex), typeof(InvalidOperationException));
        }

        // ----- Special cases -----

        [TestMethod]
        public void Build_OnBinaryOperatorPredecessor_ClonesOperandNotBuilder()
        {
            // The runtime state of a binary operator lives in its Operand, a
            // WorkflowProperty, not on the builder itself. The decorator should
            // therefore clone the Operand and not the builder.
            var addBuilder = new AddBuilder();
            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .Append(addBuilder)
                .Append(new IsolateBuilder())
                .AppendOutput();

            var expression = workflow.Workflow.Build();
            Assert.IsNotNull(addBuilder.Operand);

            var collector = new ParameterTypeCollector();
            collector.Visit(expression);
            Assert.IsTrue(collector.Types.Any(t => typeof(WorkflowProperty).IsAssignableFrom(t)),
                "Expected a WorkflowProperty parameter in the compiled expression; the Operand of the binary operator was not cloned.");
            Assert.IsFalse(collector.Types.Any(t => t == typeof(AddBuilder)),
                "The binary operator builder itself was cloned; only its Operand should have been.");
        }

        [TestMethod]
        public void Build_NestedScopeContainingDisabledBuilder_DisabledBuilderNotCloned()
        {
            // Disabled nodes inside a nested scope are skipped during cloning.
            // The decorator does not produce a clone parameter for them, even
            // when other operators in the same scope are cloned.
            var enabledCollector = new ValueCollector();
            var disabledCollector = new ValueCollector();
            var disabledBuilder = new DisableBuilder(new CombinatorBuilder { Combinator = disabledCollector });

            var workflow = new TestWorkflow()
                .AppendCombinator(new Reactive.Range { Count = 2 })
                .AppendNested(
                    input => input
                        .AppendCombinator(enabledCollector)
                        .Append(disabledBuilder)
                        .AppendOutput(),
                    workflow => new Defer(workflow))
                .Append(new IsolateBuilder())
                .AppendOutput();

            var expression = workflow.Workflow.Build();
            var collector = new ParameterTypeCollector();
            collector.Visit(expression);

            Assert.IsTrue(collector.Types.Contains(typeof(ValueCollector)),
                "The enabled ValueCollector was not substituted; the graph walk regressed on the live case.");
            // Both the enabled and disabled collectors are of type ValueCollector,
            // so checking the parameter types alone is insufficient. Confirm by
            // inspecting the block variables: exactly one ValueCollector clone
            // parameter should be present.
            var defer = (MethodCallExpression)expression;
            var selector = defer.Arguments.OfType<LambdaExpression>().Single();
            var block = (BlockExpression)selector.Body;
            var valueCollectorLocals = block.Variables.Count(v => v.Type == typeof(ValueCollector));
            Assert.AreEqual(1, valueCollectorLocals,
                "Expected exactly one ValueCollector clone parameter; a disabled node was cloned.");
        }
    }
}

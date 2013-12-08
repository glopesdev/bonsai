﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;
using System.Xml.Serialization;
using System.ComponentModel;
using Bonsai.Dag;
using System.Reactive;
using System.Reactive.Linq;

namespace Bonsai.Expressions
{
    [WorkflowElementCategory(ElementCategory.Nested)]
    [XmlType("Workflow", Namespace = Constants.XmlNamespace)]
    [TypeDescriptionProvider(typeof(WorkflowTypeDescriptionProvider))]
    public abstract class WorkflowExpressionBuilder : ExpressionBuilder, INamedElement
    {
        readonly ExpressionBuilderGraph workflow;

        protected WorkflowExpressionBuilder()
            : this(new ExpressionBuilderGraph())
        {
        }

        protected WorkflowExpressionBuilder(ExpressionBuilderGraph workflow)
        {
            if (workflow == null)
            {
                throw new ArgumentNullException("workflow");
            }

            this.workflow = workflow;
        }

        [Description("The name of the encapsulated workflow.")]
        public string Name { get; set; }

        [XmlIgnore]
        [Browsable(false)]
        public ExpressionBuilderGraph Workflow
        {
            get { return workflow; }
        }

        [Browsable(false)]
        [XmlElement("Workflow")]
        public ExpressionBuilderGraphDescriptor WorkflowDescriptor
        {
            get { return workflow.ToDescriptor(); }
            set
            {
                workflow.Clear();
                workflow.AddDescriptor(value);
            }
        }

        internal BuildContext BuildContext { get; set; }

        protected Expression BuildWorflow(Expression source, Func<Expression, Expression> selector)
        {
            // Assign source if available
            var workflowInput = Workflow.Select(node => node.Value as WorkflowInputBuilder)
                                        .SingleOrDefault(builder => builder != null);
            if (workflowInput != null)
            {
                if (source == null)
                {
                    throw new ArgumentNullException("source");
                }

                workflowInput.Source = source;
            }

            var buildContext = BuildContext;
            var expression = Workflow.Build(buildContext);
            if (buildContext != null && buildContext.BuildResult != null) return buildContext.BuildResult;
            return selector(expression);
        }
    }
}

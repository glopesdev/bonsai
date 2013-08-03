﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;
using System.ComponentModel;
using System.Xml.Serialization;
using System.Reflection;

namespace Bonsai.Expressions
{
    [XmlInclude(typeof(CombinatorExpressionBuilder))]
    [XmlInclude(typeof(WorkflowExpressionBuilder))]
    [XmlInclude(typeof(WorkflowInputBuilder))]
    [XmlInclude(typeof(WorkflowOutputBuilder))]
    [XmlInclude(typeof(SourceBuilder))]
    [XmlInclude(typeof(TransformBuilder))]
    [XmlInclude(typeof(ConditionBuilder))]
    [XmlInclude(typeof(SinkBuilder))]
    [XmlInclude(typeof(CombinatorBuilder))]
    [XmlInclude(typeof(NullSinkBuilder))]
    [XmlInclude(typeof(MemberSelectorBuilder))]
    [XmlInclude(typeof(SelectManyBuilder))]
    [XmlInclude(typeof(WindowWorkflowBuilder))]
    [XmlType("Expression", Namespace = Constants.XmlNamespace)]
    [TypeConverter("Bonsai.Design.ExpressionBuilderTypeConverter, Bonsai.Design")]
    public abstract class ExpressionBuilder
    {
        public abstract Expression Build();

        public static Type GetWorkflowElementType(ExpressionBuilder builder)
        {
            var sourceBuilder = builder as SourceBuilder;
            if (sourceBuilder != null) return sourceBuilder.Source.GetType();

            var selectBuilder = builder as TransformBuilder;
            if (selectBuilder != null) return selectBuilder.Transform.GetType();

            var whereBuilder = builder as ConditionBuilder;
            if (whereBuilder != null) return whereBuilder.Condition.GetType();

            var doBuilder = builder as SinkBuilder;
            if (doBuilder != null) return doBuilder.Sink.GetType();

            var combinatorBuilder = builder as CombinatorBuilder;
            if (combinatorBuilder != null) return combinatorBuilder.Combinator.GetType();

            return builder.GetType();
        }

        public static ExpressionBuilder FromLoadableElement(LoadableElement element, ElementCategory elementType)
        {
            if (element == null)
            {
                throw new ArgumentNullException("element");
            }

            if (elementType == ElementCategory.Source) return new SourceBuilder { Source = element };
            if (elementType == ElementCategory.Condition) return new ConditionBuilder { Condition = element };
            if (elementType == ElementCategory.Transform) return new TransformBuilder { Transform = element };
            if (elementType == ElementCategory.Combinator) return new CombinatorBuilder { Combinator = element };
            if (elementType == ElementCategory.Sink) return new SinkBuilder { Sink = element };
            throw new InvalidOperationException("Invalid loadable element type.");
        }

        protected static Type GetObservableType(object source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            return source.GetType()
                         .FindInterfaces((t, m) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IObservable<>), null)
                         .First()
                         .GetGenericArguments()[0];
        }

        internal static Type[] GetMethodBindings(MethodInfo methodInfo, params Type[] parameters)
        {
            if (methodInfo == null)
            {
                throw new ArgumentNullException("methodInfo");
            }

            if (parameters == null)
            {
                throw new ArgumentNullException("parameters");
            }

            var methodParameters = methodInfo.GetParameters();
            var methodGenericArguments = methodInfo.GetGenericArguments();

            // The binding candidates are the distinct results from matching parameters with input
            // Matches for the same generic parameter position should be identical
            var bindingCandidates = (from bindings in methodParameters.Zip(parameters, (methodParameter, parameter) => GetParameterBindings(methodParameter.ParameterType, parameter))
                                     from binding in bindings
                                     group binding by binding.Item2 into matches
                                     orderby matches.Key ascending
                                     select matches.Distinct().Single().Item1)
                                     .ToArray();

            return methodGenericArguments.Zip(bindingCandidates, (argument, match) => match).Concat(methodGenericArguments.Skip(bindingCandidates.Length)).ToArray();
        }

        internal static IEnumerable<Tuple<Type, int>> GetParameterBindings(Type parameterType, Type inputType)
        {
            // If parameter is a generic parameter, just bind it to the input type
            if (parameterType.IsGenericParameter)
            {
                return Enumerable.Repeat(Tuple.Create(inputType, parameterType.GenericParameterPosition), 1);
            }
            // If parameter contains generic parameters, we may have possible bindings
            else if (parameterType.ContainsGenericParameters)
            {
                // Check if we have a straight type match
                var bindings = MatchTypeBindings(parameterType, inputType).ToArray();
                if (bindings.Length > 0) return bindings;

                // Direct match didn't produce any bindings, so we need to check inheritance chain
                Type currentType = inputType;
                while (currentType != typeof(object))
                {
                    currentType = currentType.BaseType;
                    bindings = MatchTypeBindings(parameterType, currentType).ToArray();
                    if (bindings.Length > 0) return bindings;
                }

                // Inheritance chain match didn't produce any bindings, so we need to check interface set
                var interfaces = inputType.GetInterfaces();
                foreach (var interfaceType in interfaces)
                {
                    bindings = MatchTypeBindings(parameterType, interfaceType).ToArray();
                    if (bindings.Length > 0) return bindings;
                }
            }

            // If parameter does not contain generic parameters, there's nothing to bind to (check for error?)
            return Enumerable.Empty<Tuple<Type, int>>();
        }

        internal static IEnumerable<Tuple<Type, int>> MatchTypeBindings(Type parameterType, Type inputType)
        {
            // Match bindings can only be obtained if both types are generic types
            if (parameterType.IsGenericType && inputType.IsGenericType)
            {
                var parameterTypeDefinition = parameterType.GetGenericTypeDefinition();
                var inputTypeDefinition = parameterType.GetGenericTypeDefinition();
                // Match bindings can only be obtained if both types share the same type definition
                if (parameterTypeDefinition == inputTypeDefinition)
                {
                    var parameterGenericArguments = parameterType.GetGenericArguments();
                    var inputGenericArguments = inputType.GetGenericArguments();
                    return parameterGenericArguments
                        .Zip(inputGenericArguments, (parameter, input) => GetParameterBindings(parameter, input))
                        .SelectMany(xs => xs);
                }
            }

            return Enumerable.Empty<Tuple<Type, int>>();
        }

        internal static Expression BuildProcessExpression(object processor, MethodInfo processMethod, params Expression[] parameters)
        {
            if (processMethod.IsGenericMethodDefinition)
            {
                var typeArguments = GetMethodBindings(processMethod, Array.ConvertAll(parameters, xs => xs.Type));
                processMethod = processMethod.MakeGenericMethod(typeArguments);
            }

            int i = 0;
            var processorExpression = Expression.Constant(processor);
            var processParameters = processMethod.GetParameters();
            parameters = Array.ConvertAll(parameters, parameter =>
            {
                var parameterType = processParameters[i++].ParameterType;
                if (parameter.Type != parameterType)
                {
                    return Expression.Convert(parameter, parameterType);
                }
                return parameter;
            });

            return Expression.Call(processorExpression, processMethod, parameters);
        }
    }
}

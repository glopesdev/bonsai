﻿using System;
using System.ComponentModel;
using System.Linq.Expressions;

namespace Bonsai.Expressions
{
    class WorkflowPropertyDescriptor : PropertyDescriptor
    {
        readonly Type componentType;
        readonly Type propertyType;
        readonly Func<object> getValue;
        readonly Action<object> setValue;

        public WorkflowPropertyDescriptor(string name, Attribute[] attrs, object property)
            : base(name, attrs)
        {
            var propertyExpression = Expression.Constant(property);
            var valueProperty = ExpressionHelper.Property(propertyExpression, "Value");
            var getterBody = Expression.Convert(valueProperty, typeof(object));
            getValue = Expression.Lambda<Func<object>>(getterBody).Compile();

            componentType = propertyExpression.Type;
            propertyType = valueProperty.Type;
            var setterParameter = Expression.Parameter(typeof(object));
            var setterBody = Expression.Assign(valueProperty, Expression.Convert(setterParameter, propertyType));
            setValue = Expression.Lambda<Action<object>>(setterBody, setterParameter).Compile();
        }

        public override bool CanResetValue(object component)
        {
            return false;
        }

        public override Type ComponentType
        {
            get { return componentType; }
        }

        public override object GetValue(object component)
        {
            return getValue();
        }

        public override bool IsReadOnly
        {
            get { return false; }
        }

        public override Type PropertyType
        {
            get { return propertyType; }
        }

        public override void ResetValue(object component)
        {
        }

        public override void SetValue(object component, object value)
        {
            setValue(value);
        }

        public override bool ShouldSerializeValue(object component)
        {
            return true;
        }
    }
}

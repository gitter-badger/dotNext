﻿using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace DotNext.Dynamic
{
    using RuntimeFeaturesAttribute = Runtime.CompilerServices.RuntimeFeaturesAttribute;

    [RuntimeFeatures(DynamicCodeCompilation = true, RuntimeGenericInstantiation = true)]
    internal sealed class TaskResultBinder : CallSiteBinder
    {
        private const string PropertyName = nameof(Task<Missing>.Result);
        private const BindingFlags PropertyFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        private static Expression BindProperty(PropertyInfo resultProperty, Expression target, out Expression restrictions)
        {
            restrictions = Expression.TypeIs(target, resultProperty.DeclaringType);

            // reinterpret reference type without casting because it is protected by restriction
            target = Expression.Call(typeof(Unsafe), nameof(Unsafe.As), new[] { resultProperty.DeclaringType }, target);
            target = Expression.Property(target, resultProperty);
            return target.Type.IsValueType ? Expression.Convert(target, typeof(object)) : target;
        }

        private static Expression Bind(object targetValue, Expression target, LabelTarget returnLabel)
        {
            PropertyInfo? property = targetValue.GetType().GetProperty(PropertyName, PropertyFlags);
            Debug.Assert(!(property is null));
            target = BindProperty(property, target, out var restrictions);

            target = Expression.Return(returnLabel, target);
            target = Expression.Condition(restrictions, target, Expression.Goto(UpdateLabel));
            return target;
        }

        public override Expression Bind(object[] args, ReadOnlyCollection<ParameterExpression> parameters, LabelTarget returnLabel) => Bind(args[0], parameters[0], returnLabel);
    }
}

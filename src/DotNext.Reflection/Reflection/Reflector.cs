using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace DotNext.Reflection
{
    using static Runtime.CompilerServices.PointerHelpers;

    /// <summary>
    /// Provides access to fast reflection routines.
    /// </summary>
    public static class Reflector
    {
        private static MemberInfo? MemberOf(LambdaExpression exprTree) => exprTree.Body switch
        {
            MemberExpression expr => expr.Member,
            MethodCallExpression expr => expr.Method,
            NewExpression expr => expr.Constructor,
            BinaryExpression expr => expr.Method,
            UnaryExpression expr => expr.Method,
            IndexExpression expr => expr.Indexer,
            _ => null,
        };

        /// <summary>
        /// Extracts member metadata from expression tree.
        /// </summary>
        /// <param name="exprTree">Expression tree.</param>
        /// <typeparam name="TMember">Type of member to reflect.</typeparam>
        /// <returns>Reflected member; or <see langword="null"/>, if lambda expression doesn't reference a member.</returns>
        [Obsolete("Use overloaded generic method that allows to specify delegate type explicitly")]
        public static TMember? MemberOf<TMember>(Expression<Action> exprTree)
            where TMember : MemberInfo => MemberOf<TMember, Action>(exprTree);

        /// <summary>
        /// Extracts member metadata from expression tree.
        /// </summary>
        /// <param name="exprTree">Expression tree.</param>
        /// <typeparam name="TMember">Type of member to reflect.</typeparam>
        /// <typeparam name="TDelegate">The type of lambda expression.</typeparam>
        /// <returns>Reflected member; or <see langword="null"/>, if lambda expression doesn't reference a member.</returns>
        public static TMember? MemberOf<TMember, TDelegate>(Expression<TDelegate> exprTree)
            where TMember : MemberInfo
            where TDelegate : Delegate
            => MemberOf(exprTree) as TMember;

        /// <summary>
        /// Unreflects constructor to its typed and callable representation.
        /// </summary>
        /// <typeparam name="TDelegate">A delegate representing signature of constructor.</typeparam>
        /// <param name="ctor">Constructor to unreflect.</param>
        /// <returns>Unreflected constructor.</returns>
        public static Constructor<TDelegate>? Unreflect<TDelegate>(this ConstructorInfo ctor)
            where TDelegate : MulticastDelegate => Constructor<TDelegate>.GetOrCreate(ctor);

        /// <summary>
        /// Unreflects method to its typed and callable representation.
        /// </summary>
        /// <typeparam name="TDelegate">A delegate representing signature of method.</typeparam>
        /// <param name="method">A method to unreflect.</param>
        /// <returns>Unreflected method.</returns>
        public static Method<TDelegate>? Unreflect<TDelegate>(this MethodInfo method)
            where TDelegate : MulticastDelegate => Method<TDelegate>.GetOrCreate(method);

        /// <summary>
        /// Obtains managed pointer to the static field.
        /// </summary>
        /// <typeparam name="TValue">The field type.</typeparam>
        /// <param name="field">The field to unreflect.</param>
        /// <returns>Unreflected static field.</returns>
        public static Field<TValue> Unreflect<TValue>(this FieldInfo field) => Field<TValue>.GetOrCreate(field);

        /// <summary>
        /// Obtains managed pointer to the instance field.
        /// </summary>
        /// <typeparam name="T">The type of the object that declares instance field.</typeparam>
        /// <typeparam name="TValue">The field type.</typeparam>
        /// <param name="field">The field to unreflect.</param>
        /// <returns>Unreflected instance field.</returns>
        public static Field<T, TValue> Unreflect<T, TValue>(this FieldInfo field)
            where T : notnull => Field<T, TValue>.GetOrCreate(field);

        private static MemberExpression BuildFieldAccess(FieldInfo field, ParameterExpression target)
        {
            Expression? owner;
            if (field.IsStatic)
                owner = null;
            else if (field.DeclaringType.IsValueType)
                owner = Expression.Unbox(target, field.DeclaringType);
            else
                owner = Expression.Convert(target, field.DeclaringType);

            return Expression.Field(owner, field);
        }

        private static Expression BuildGetter(MemberExpression field)
        {
            Expression fieldAccess = field;
            if (fieldAccess.Type.IsPointer)
                fieldAccess = Wrap(fieldAccess);
            if (fieldAccess.Type.IsValueType)
                fieldAccess = Expression.Convert(fieldAccess, typeof(object));
            return fieldAccess;
        }

        private static DynamicInvoker BuildGetter(FieldInfo field)
        {
            var target = Expression.Parameter(typeof(object));
            var arguments = Expression.Parameter(typeof(object[]));
            return Expression.Lambda<DynamicInvoker>(BuildGetter(BuildFieldAccess(field, target)), target, arguments).Compile();
        }

        private static Expression BuildSetter(MemberExpression field, ParameterExpression arguments)
        {
            Expression valueArg = Expression.ArrayIndex(arguments, Expression.Constant(0));
            if (field.Type.IsPointer)
                valueArg = Unwrap(valueArg, field.Type);
            if (valueArg.Type != field.Type)
                valueArg = Expression.Convert(valueArg, field.Type);
            return Expression.Block(typeof(object), Expression.Assign(field, valueArg), Expression.Default(typeof(object)));
        }

        private static DynamicInvoker BuildSetter(FieldInfo field)
        {
            var target = Expression.Parameter(typeof(object));
            var arguments = Expression.Parameter(typeof(object[]));
            return Expression.Lambda<DynamicInvoker>(BuildSetter(BuildFieldAccess(field, target), arguments), target, arguments).Compile();
        }

        private static DynamicInvoker BuildInvoker(FieldInfo field)
        {
            var target = Expression.Parameter(typeof(object));
            var arguments = Expression.Parameter(typeof(object[]));
            var fieldAccess = BuildFieldAccess(field, target);
            var body = Expression.Condition(
                Expression.Equal(Expression.ArrayLength(arguments), Expression.Constant(0)),
                BuildGetter(fieldAccess),
                BuildSetter(fieldAccess, arguments),
                typeof(object));
            return Expression.Lambda<DynamicInvoker>(body, target, arguments).Compile();
        }

        /// <summary>
        /// Creates dynamic invoker for the field.
        /// </summary>
        /// <remarks>
        /// This method doesn't cache the result so the caller is responsible for storing delegate to the field or cache.
        /// <paramref name="flags"/> supports the following combination of values: <see cref="BindingFlags.GetField"/>, <see cref="BindingFlags.SetField"/> or
        /// both.
        /// </remarks>
        /// <param name="field">The field to unreflect.</param>
        /// <param name="flags">Describes the access to the field using invoker.</param>
        /// <returns>The delegate that can be used to access field value.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="flags"/> is invalid.</exception>
        /// <exception cref="NotSupportedException">The type of <paramref name="field"/> is ref-like value type.</exception>
        public static DynamicInvoker Unreflect(this FieldInfo field, BindingFlags flags = BindingFlags.GetField | BindingFlags.SetField)
        {
            if (field.FieldType.IsByRefLike)
                throw new NotSupportedException();
            return flags switch
            {
                BindingFlags.GetField => BuildGetter(field),
                BindingFlags.SetField => BuildSetter(field),
                BindingFlags.GetField | BindingFlags.SetField => BuildInvoker(field),
                _ => throw new ArgumentOutOfRangeException(nameof(flags))
            };
        }

        private static DynamicInvoker Unreflect(MethodBase method, Action<ILGenerator> methodCall)
        {
            Type? parameterType = method.DeclaringType;

            var builder = new DynamicMethod(method.ToString(), typeof(object), new[] { typeof(object), typeof(object[]) }, true);
            var generator = builder.GetILGenerator();

            // push this arg
            if (method.IsConstructor || method.IsStatic || parameterType is null)
            {
            }
            else if (parameterType.IsValueType)
            {
                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Unbox, parameterType);
            }
            else
            {
                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Castclass, parameterType);
            }

            // push parameters
            foreach (var parameter in method.GetParameters())
            {
                generator.Emit(OpCodes.Ldarg_1);
                generator.Emit(OpCodes.Ldc_I4, parameter.Position);
                generator.Emit(OpCodes.Conv_I);
                if (parameter.ParameterType.IsByRefLike)
                {
                    throw new NotSupportedException();
                }
                else if (parameter.ParameterType.IsByRef)
                {
                    parameterType = parameter.ParameterType.GetElementType();
                    if (parameterType.IsPointer)
                    {
                        generator.Emit(OpCodes.Ldelem, typeof(object));
                        generator.Emit(OpCodes.Unbox, typeof(IntPtr));
                    }
                    else if (parameterType.IsValueType)
                    {
                        generator.Emit(OpCodes.Ldelem, typeof(object));
                        generator.Emit(OpCodes.Unbox, parameterType);
                    }
                    else
                    {
                        generator.Emit(OpCodes.Ldelema, typeof(object));
                        generator.Emit(OpCodes.Call, AsTypedReference(parameterType));
                    }
                }
                else if (parameter.ParameterType.IsPointer)
                {
                    generator.Emit(OpCodes.Ldelem, typeof(object));
                    generator.Emit(OpCodes.Call, UnboxPointerMethod);
                }
                else if (parameter.ParameterType.IsValueType)
                {
                    generator.Emit(OpCodes.Ldelem, typeof(object));
                    generator.Emit(OpCodes.Unbox_Any, parameter.ParameterType);
                }
                else if (parameter.ParameterType == typeof(object))
                {
                    generator.Emit(OpCodes.Ldelem, typeof(object));
                }
                else
                {
                    generator.Emit(OpCodes.Ldelem, typeof(object));
                    generator.Emit(OpCodes.Castclass, parameter.ParameterType);
                }
            }

            // invoke method
            methodCall(generator);
            generator.Emit(OpCodes.Ret);

            return builder.CreateDelegate<DynamicInvoker>();
        }

        private static void GenerateMethodCall(this MethodInfo method, ILGenerator generator)
        {
            var callCode = method.IsStatic || method.DeclaringType.IsValueType ?
                OpCodes.Call :
                OpCodes.Callvirt;

            // for void return type it's necessary to return null. Tail call is not applicable.
            if (method.ReturnType == typeof(void))
            {
                generator.Emit(callCode, method);
                generator.Emit(OpCodes.Ldnull);
            }
            else if (method.ReturnType.IsPointer)
            {
                // pointer value must be boxed after actual call. Tail call is not applicable.
                generator.Emit(callCode, method);
                generator.Emit(OpCodes.Ldtoken, method.ReturnType);
                generator.Emit(OpCodes.Call, GetTypeFromHandleMethod);
                generator.Emit(OpCodes.Call, BoxPointerMethod);
            }
            else if (method.ReturnType.IsValueType)
            {
                // returned value type must be boxed. Tail call is not applicable.
                generator.Emit(callCode, method);
                generator.Emit(OpCodes.Box, method.ReturnType);
            }
            else
            {
                generator.Emit(OpCodes.Tailcall);
                generator.Emit(callCode, method);
            }
        }

        /// <summary>
        /// Creates dynamic invoker for the method.
        /// </summary>
        /// <param name="method">The method to unreflect.</param>
        /// <returns>The delegate that can be used to invoke the method.</returns>
        /// <exception cref="NotSupportedException">The type of parameter or return type is ref-like value type.</exception>
        public static DynamicInvoker Unreflect(this MethodInfo method)
            => Unreflect(method, method.GenerateMethodCall);

        private static void GenerateCtorCall(this ConstructorInfo ctor, ILGenerator generator)
        {
            generator.Emit(OpCodes.Newobj, ctor);
            if (ctor.DeclaringType.IsValueType)
                generator.Emit(OpCodes.Box, ctor.DeclaringType);
        }

        /// <summary>
        /// Creates dynamic invoker for the constructor.
        /// </summary>
        /// <param name="ctor">The constructor to unreflect.</param>
        /// <returns>The delegate that can be used to create an object instance.</returns>
        /// <exception cref="NotSupportedException">The type of parameter is ref-like value type.</exception>
        public static DynamicInvoker Unreflect(this ConstructorInfo ctor)
            => Unreflect(ctor, ctor.GenerateCtorCall);
    }
}
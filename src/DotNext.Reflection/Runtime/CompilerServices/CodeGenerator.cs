using System;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using static InlineIL.IL;
using static InlineIL.IL.Emit;

namespace DotNext.Runtime.CompilerServices
{
    internal static class CodeGenerator
    {
        internal static MethodInfo BoxPointerMethod
            => typeof(Pointer).GetMethod(nameof(Pointer.Box), new[] { typeof(void*), typeof(Type) });

        internal static MethodInfo GetTypeFromHandleMethod
            => typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), new[] { typeof(RuntimeTypeHandle) });

        internal static MethodInfo UnboxPointerMethod
            => typeof(Pointer).GetMethod(nameof(Pointer.Unbox), new[] { typeof(object) });

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T? AsTypedReference<T>(ref object? item)
            where T : class
        {
            Push(ref item);
            Dup();
            Ldind_Ref();
            Castclass<T>();
            Pop();
            return ref ReturnRef<T?>();
        }

        internal static MethodInfo AsTypedReference(Type typeToken)
            => typeof(CodeGenerator).GetMethod(nameof(AsTypedReference), 1, new[] { typeof(object).MakeByRefType() }).MakeGenericMethod(typeToken);

        internal static unsafe object Wrap<T>(T* ptr)
            where T : unmanaged => Pointer.Box(ptr, typeof(T*));

        internal static unsafe T* Unwrap<T>(object ptr)
            where T : unmanaged => (T*)Pointer.Unbox(ptr);

        internal static Expression Wrap(Expression expression)
        {
            Debug.Assert(expression.Type.IsPointer);
            var elementType = expression.Type.GetElementType();
            if (elementType == typeof(void))
                return Expression.Call(typeof(Pointer), nameof(Pointer.Box), Array.Empty<Type>(), expression, Expression.Constant(typeof(void*)));
            return Expression.Call(typeof(CodeGenerator), nameof(Wrap), new[] { elementType }, expression);
        }

        internal static Expression Unwrap(Expression expression, Type expectedType)
        {
            Debug.Assert(expectedType.IsPointer);
            var elementType = expectedType.GetElementType();
            if (elementType == typeof(void))
                return Expression.Call(typeof(Pointer), nameof(Pointer.Unbox), Array.Empty<Type>(), expression);
            return Expression.Call(typeof(CodeGenerator), nameof(Unwrap), new[] { elementType }, expression);
        }
    }
}

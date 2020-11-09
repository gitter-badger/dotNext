using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using static InlineIL.IL;
using static InlineIL.IL.Emit;

namespace DotNext.Runtime.CompilerServices
{
    internal static class CodeGenerator
    {
        internal static void LoadField(this ILGenerator generator, FieldInfo field, bool volatileAccess = false)
        {
            if (volatileAccess)
                generator.Emit(OpCodes.Volatile);
            if (field.IsStatic || field.DeclaringType is null)
                generator.Emit(OpCodes.Ldsfld, field);
            else
                generator.Emit(OpCodes.Ldfld, field);
        }

        internal static void StoreField(this ILGenerator generator, FieldInfo field, bool volatileAccess = false)
        {
            if (volatileAccess)
                generator.Emit(OpCodes.Volatile);
            if (field.IsStatic || field.DeclaringType is null)
                generator.Emit(OpCodes.Stsfld, field);
            else
                generator.Emit(OpCodes.Stfld, field);
        }

        private static MethodInfo BoxPointerMethod
            => typeof(Pointer).GetMethod(nameof(Pointer.Box), new[] { typeof(void*), typeof(Type) });

        internal static void BoxPointer(this ILGenerator generator, Type pointerType)
        {
            Debug.Assert(pointerType.IsPointer);
            generator.LoadType(pointerType);
            generator.Emit(OpCodes.Call, BoxPointerMethod);
        }

        private static MethodInfo GetTypeFromHandleMethod
            => typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), new[] { typeof(RuntimeTypeHandle) });

        internal static void LoadType(this ILGenerator generator, Type typeToken)
        {
            generator.Emit(OpCodes.Ldtoken, typeToken);
            generator.Emit(OpCodes.Call, GetTypeFromHandleMethod);
        }

        private static MethodInfo UnboxPointerMethod
            => typeof(Pointer).GetMethod(nameof(Pointer.Unbox), new[] { typeof(object) });

        internal static void UnboxPointer(this ILGenerator generator)
        {
            generator.Emit(OpCodes.Call, UnboxPointerMethod);
        }

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

        internal static void AsTypedReference(this ILGenerator generator, Type typeToken)
        {
            generator.Emit(OpCodes.Call, typeof(CodeGenerator).GetMethod(nameof(AsTypedReference), 1, new[] { typeof(object).MakeByRefType() }).MakeGenericMethod(typeToken));
        }
    }
}

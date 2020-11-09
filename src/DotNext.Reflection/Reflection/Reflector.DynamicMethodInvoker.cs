using System;
using System.Reflection;
using System.Reflection.Emit;

namespace DotNext.Reflection
{
    using static Runtime.CompilerServices.CodeGenerator;

    public partial class Reflector
    {
        // TODO: methodCall must be replaced with method pointer
        private static DynamicInvoker Unreflect(MethodBase method, Action<ILGenerator> methodCall)
        {
            Type? parameterType = method.DeclaringType;

            var builder = new DynamicMethod(method.ToString(), typeof(object), new[] { typeof(object), typeof(object[]) }, true);
            var generator = builder.GetILGenerator();

            // push 'this' arg
            if (method.IsConstructor || method.IsStatic || parameterType is null)
            {
                // constructor, static method and module method don't have 'this' arg
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
                        generator.AsTypedReference(parameterType);
                    }
                }
                else if (parameter.ParameterType.IsPointer)
                {
                    generator.Emit(OpCodes.Ldelem, typeof(object));
                    generator.UnboxPointer();
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
                generator.BoxPointer(method.ReturnType);
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

        private static void GenerateCtorCall(this ConstructorInfo ctor, ILGenerator generator)
        {
            generator.Emit(OpCodes.Newobj, ctor);
            if (ctor.DeclaringType.IsValueType)
                generator.Emit(OpCodes.Box, ctor.DeclaringType);
        }
    }
}
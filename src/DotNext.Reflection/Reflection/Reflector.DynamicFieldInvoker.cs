using System.Reflection;
using System.Reflection.Emit;

namespace DotNext.Reflection
{
    using static Runtime.CompilerServices.CodeGenerator;

    public partial class Reflector
    {
        private static void GenerateFieldOwner(this ILGenerator generator, FieldInfo field)
        {
            // prepare 'this' arg
            if (field.IsStatic || field.DeclaringType is null)
            {
            }
            else if (field.DeclaringType.IsValueType)
            {
                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Unbox, field.DeclaringType);
            }
            else
            {
                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Castclass, field.DeclaringType);
            }
        }

        private static void GenerateFieldGetter(this ILGenerator generator, FieldInfo field, bool volatileAccess)
        {
            generator.LoadField(field, volatileAccess);

            // convert field value to object
            if (field.FieldType.IsValueType)
            {
                generator.Emit(OpCodes.Box, field.FieldType);
            }
            else if (field.FieldType.IsPointer)
            {
                generator.BoxPointer(field.FieldType);
            }

            // return loaded value
            generator.Emit(OpCodes.Ret);
        }

        private static DynamicInvoker GenerateFieldGetter(FieldInfo field, bool volatileAccess)
        {
            var builder = new DynamicMethod(field.ToString(), typeof(object), new[] { typeof(object), typeof(object[]) }, true);
            var generator = builder.GetILGenerator();
            generator.GenerateFieldOwner(field);
            generator.GenerateFieldGetter(field, volatileAccess);

            return builder.CreateDelegate<DynamicInvoker>();
        }

        private static void GenerateFieldSetter(this ILGenerator generator, FieldInfo field, bool volatileAccess)
        {
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Ldc_I4_0);
            generator.Emit(OpCodes.Conv_I);
            generator.Emit(OpCodes.Ldelem, typeof(object));

            // load new field value
            if (field.FieldType.IsValueType)
            {
                generator.Emit(OpCodes.Unbox_Any, field.FieldType);
            }
            else if (field.FieldType.IsPointer)
            {
                generator.UnboxPointer();
            }

            // store field value
            generator.StoreField(field, volatileAccess);

            // return null for setter
            generator.Emit(OpCodes.Ldnull);
            generator.Emit(OpCodes.Ret);
        }

        private static DynamicInvoker GenerateFieldSetter(FieldInfo field, bool volatileAccess)
        {
            var builder = new DynamicMethod(field.ToString(), typeof(object), new[] { typeof(object), typeof(object[]) }, true);
            var generator = builder.GetILGenerator();
            generator.GenerateFieldOwner(field);
            generator.GenerateFieldSetter(field, volatileAccess);

            return builder.CreateDelegate<DynamicInvoker>();
        }

        private static DynamicInvoker GenerateFieldAccess(FieldInfo field, bool volatileAccess)
        {
            var builder = new DynamicMethod(field.ToString(), typeof(object), new[] { typeof(object), typeof(object[]) }, true);
            var generator = builder.GetILGenerator();

            // pushes 'this'
            generator.GenerateFieldOwner(field);

            // get arguments length
            // if zero then it's a getter
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Ldlen);
            var setterLavel = generator.DefineLabel();
            generator.Emit(OpCodes.Brtrue, setterLavel);

            // getter
            generator.GenerateFieldGetter(field, volatileAccess);

            // setter
            generator.MarkLabel(setterLavel);
            generator.GenerateFieldSetter(field, volatileAccess);

            return builder.CreateDelegate<DynamicInvoker>();
        }
    }
}
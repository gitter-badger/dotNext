﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Reflection;
using Xunit;
using static System.Runtime.CompilerServices.Unsafe;

namespace DotNext.Reflection
{
    [ExcludeFromCodeCoverage]
    public sealed class DynamicInvocationTests : Test
    {
        private sealed class MyClass
        {
            public int ValueTypeField;
            public string ObjectField;
            public unsafe byte* TypedPointerField;
            public unsafe void* UntypedPointerField;
        }

        private struct MyType
        {
            public int X, Y;
        }

        [Fact]
        public static void StaticFieldGet()
        {
            var staticField = typeof(string).GetField(nameof(string.Empty));
            var reader = staticField.Unreflect(BindingFlags.GetField);
            Same(string.Empty, reader(null));
        }

        private static void ObjectFieldTest(DynamicInvoker reader, DynamicInvoker writer)
        {
            var obj = new MyClass();
            Null(writer(obj, "Hello, world"));

            Equal("Hello, world", reader(obj));
        }

        [Fact]
        public static void ObjectFieldGetSet()
        {
            var field = typeof(MyClass).GetField(nameof(MyClass.ObjectField));
            var reader = field.Unreflect(BindingFlags.GetField);
            var writer = field.Unreflect(BindingFlags.SetField);
            ObjectFieldTest(reader, writer);
            reader = writer = field.Unreflect();
            ObjectFieldTest(reader, writer);
        }

        [Fact]
        public static void InvalidFlags()
        {
            var field = typeof(MyClass).GetField(nameof(MyClass.ValueTypeField));
            Throws<ArgumentOutOfRangeException>(() => field.Unreflect(BindingFlags.GetProperty));
        }

        private static void ValueTypeFieldTest(DynamicInvoker reader, DynamicInvoker writer)
        {
            var obj = new MyClass();
            Null(writer(obj, 42));

            Equal(42, reader(obj));
        }

        [Fact]
        public static void ValueTypeFieldGetSet()
        {
            var field = typeof(MyClass).GetField(nameof(MyClass.ValueTypeField));
            var reader = field.Unreflect(BindingFlags.GetField);
            var writer = field.Unreflect(BindingFlags.SetField);
            ValueTypeFieldTest(reader, writer);
            reader = writer = field.Unreflect();
            ValueTypeFieldTest(reader, writer);
        }

        private static unsafe void TypedPointerFieldTest(DynamicInvoker reader, DynamicInvoker writer)
        {
            var obj = new MyClass() { TypedPointerField = (byte*)new IntPtr(42) };
            Equal(new IntPtr(42), new IntPtr(Pointer.Unbox(reader(obj))));

            Null(writer(obj, Pointer.Box(new IntPtr(56).ToPointer(), typeof(byte*))));
            Equal(new IntPtr(56), new IntPtr(Pointer.Unbox(reader(obj))));
        }

        [Fact]
        public static void TypedPointerFieldGetSet()
        {
            var field = typeof(MyClass).GetField(nameof(MyClass.TypedPointerField));
            NotNull(field);
            var reader = field.Unreflect(BindingFlags.GetField);
            var writer = field.Unreflect(BindingFlags.SetField);
            TypedPointerFieldTest(reader, writer);
            reader = writer = field.Unreflect();
            TypedPointerFieldTest(reader, writer);
        }

        private static unsafe void PointerFieldTest(DynamicInvoker reader, DynamicInvoker writer)
        {
            var obj = new MyClass() { UntypedPointerField = (void*)new IntPtr(42) };
            Equal(new IntPtr(42), new IntPtr(Pointer.Unbox(reader(obj))));

            Null(writer(obj, Pointer.Box(new IntPtr(56).ToPointer(), typeof(void*))));
            Equal(new IntPtr(56), new IntPtr(Pointer.Unbox(reader(obj))));
        }

        [Fact]
        public static unsafe void PointerFieldGetSet()
        {
            var field = typeof(MyClass).GetField(nameof(MyClass.UntypedPointerField));
            NotNull(field);
            var reader = field.Unreflect(BindingFlags.GetField);
            var writer = field.Unreflect(BindingFlags.SetField);
            PointerFieldTest(reader, writer);
            reader = writer = field.Unreflect();
            PointerFieldTest(reader, writer);
        }

        [Fact]
        public static void MethodDynamicInvoke()
        {
            var method = typeof(int).GetMethod(nameof(int.ToString), new[] { typeof(string) }).Unreflect();
            Equal("C", method(12, "X"));
            method = typeof(int).GetMethod(nameof(int.TryParse), new[] { typeof(string), typeof(int).MakeByRefType() }).Unreflect();
            object[] args = { "123", 0 };
            Equal(true, method(null, args));
            Equal(123, args[1]);
        }

        [Fact]
        public static unsafe void OperatorDynamicInvoke()
        {
            var method = typeof(IntPtr).GetMethod(nameof(IntPtr.ToPointer), Array.Empty<Type>()).Unreflect();
            Equal(new IntPtr(42), new IntPtr(Pointer.Unbox(method(new IntPtr(42)))));

            method = typeof(IntPtr).GetMethod("op_Explicit", new[] { typeof(void*) }).Unreflect();
            Equal(new IntPtr(42), (IntPtr)method(null, Pointer.Box(new IntPtr(42).ToPointer(), typeof(void*))));
        }

        [Fact]
        public static void InstantiateDynamically()
        {
            var ctor = typeof(string).GetConstructor(new[] { typeof(char), typeof(int) }).Unreflect();
            Equal("aaa", ctor(null, 'a', 3));
        }

        [Fact]
        public static void InstantiateValueType()
        {
            var ctor = typeof(IntPtr).GetConstructor(new[] { typeof(int) }).Unreflect();
            Equal(new IntPtr(42), ctor(null, 42));
        }

        [Fact]
        public static void CallByRefMethod()
        {
            var invoker = new RefAction<string, object>(SetString).Method.Unreflect();
            object[] args = { null, 42 };
            Null(invoker(null, args));
            Equal("42", args[0]);

            static void SetString(ref string value, object obj) => value = obj.ToString();
        }

        [Fact]
        public static void ModifyPropertyByRef()
        {
            object point = new Point();
            var setter = point.GetType().GetProperty(nameof(Point.X)).SetMethod.Unreflect();
            setter(point, 42);
            Equal(42, Unbox<Point>(point).X);
        }

        [Fact]
        public static void ModifyFieldByRef()
        {
            object value = new MyType();
            var setter = value.GetType().GetField(nameof(MyType.X)).Unreflect(BindingFlags.SetField);
            setter(value, 42);
            Equal(42, Unbox<MyType>(value).X);
        }
    }
}

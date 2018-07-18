﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using GraphQL.EntityFramework;

static class ExpressionBuilder<T>
{
    static ConcurrentDictionary<string, PropertyAccessor> funcs = new ConcurrentDictionary<string, PropertyAccessor>();

    public static Expression<Func<T, bool>> BuildPredicate(WhereExpression where)
    {
        return BuildPredicate(where.Path, where.Comparison, where.Value, where.Case);
    }

    public static Expression<Func<T, object>> BuildPropertyExpression(string path)
    {
        var propertyFunc = GetPropertyFunc(path);
        var propAsObject = Expression.Convert(propertyFunc.Left, typeof(object));

        return Expression.Lambda<Func<T, object>>(propAsObject, propertyFunc.SourceParameter);
    }

    public static Expression<Func<T, bool>> BuildPredicate(string path, Comparison comparison, string[] values, StringComparison? stringComparison = null)
    {
        var propertyFunc = GetPropertyFunc(path);

        if (propertyFunc.Type == typeof(string))
        {
            WhereValidator.ValidateString(comparison, stringComparison);
            var stringComparisonValue = stringComparison.GetValueOrDefault(StringComparison.OrdinalIgnoreCase);
            if (comparison == Comparison.In)
            {
                return BuildStringIn(values, propertyFunc, stringComparisonValue);
            }
            else
            {
                var value = values?.Single();
                return BuildStringCompare(comparison, value, propertyFunc, stringComparisonValue);
            }
        }
        else
        {
            WhereValidator.ValidateObject(propertyFunc.Type, comparison, stringComparison);
            if (comparison == Comparison.In)
            {
                return BuildObjectIn(values, propertyFunc);
            }
            else
            {
                var value = values?.Single();
                return BuildObjectCompare(comparison, value, propertyFunc);
            }
        }
    }

    static Expression<Func<T, bool>> BuildStringCompare(Comparison comparison, string value, PropertyAccessor propertyAccessor, StringComparison stringComparison)
    {
        var body = MakeStringComparison(propertyAccessor.Left, comparison, value, stringComparison);
        return Expression.Lambda<Func<T, bool>>(body, propertyAccessor.SourceParameter);
    }

    static Expression<Func<T, bool>> BuildObjectCompare(Comparison comparison, string expressionValue, PropertyAccessor propertyAccessor)
    {
        var valueObject = TypeConverter.ConvertStringToType(expressionValue, propertyAccessor.Type);
        var body = MakeObjectComparison(propertyAccessor.Left, comparison, valueObject);
        return Expression.Lambda<Func<T, bool>>(body, propertyAccessor.SourceParameter);
    }

    static PropertyAccessor GetPropertyFunc(string propertyPath)
    {
        return funcs.GetOrAdd(propertyPath, x =>
        {
            var parameter = Expression.Parameter(typeof(T));
            var aggregatePath = AggregatePath(x, parameter);
            return new PropertyAccessor
            {
                SourceParameter = parameter,
                Left = aggregatePath,
                Type = aggregatePath.Type
            };
        });
    }

    static Expression<Func<T, bool>> BuildObjectIn(string[] values, PropertyAccessor propertyAccessor)
    {
        var objects = TypeConverter.ConvertStringsToList(values, propertyAccessor.Type);
        var constant = Expression.Constant(objects);
        var inInfo = objects.GetType().GetMethod("Contains", new[] {propertyAccessor.Type});
        var body = Expression.Call(constant, inInfo, propertyAccessor.Left);
        return Expression.Lambda<Func<T, bool>>(body, propertyAccessor.SourceParameter);
    }

    static Expression<Func<T, bool>> BuildStringIn(string[] array, PropertyAccessor propertyAccessor, StringComparison stringComparison)
    {
        var itemValue = Expression.Parameter(typeof(string));
        var equalsBody = Expression.Call(null, StringMethodCache.Equal, itemValue, propertyAccessor.Left, Expression.Constant(stringComparison));
        var itemEvaluate = Expression.Lambda<Func<string, bool>>(equalsBody, itemValue);
        var anyBody = Expression.Call(null, StringMethodCache.Any, Expression.Constant(array), itemEvaluate);
        return Expression.Lambda<Func<T, bool>>(anyBody, propertyAccessor.SourceParameter);
    }

    static Expression MakeStringComparison(Expression left, Comparison comparison, string value, StringComparison stringComparison)
    {
        var valueConstant = Expression.Constant(value, typeof(string));
        var comparisonConstant = Expression.Constant(stringComparison, typeof(StringComparison));
        var nullCheck = Expression.NotEqual(left, Expression.Constant(null, typeof(object)));
        switch (comparison)
        {
            case Comparison.Equal:
                return Expression.Call(StringMethodCache.Equal, left, valueConstant, comparisonConstant);
            case Comparison.NotEqual:
                var notEqualsCall = Expression.Call(StringMethodCache.Equal, left, valueConstant, comparisonConstant);
                return Expression.Not(notEqualsCall);
            case Comparison.StartsWith:
                var startsWithExpression = Expression.Call(left, StringMethodCache.StartsWith, valueConstant, comparisonConstant);
                return Expression.AndAlso(nullCheck, startsWithExpression);
            case Comparison.EndsWith:
                var endsWithExpression = Expression.Call(left, StringMethodCache.EndsWith, valueConstant, comparisonConstant);
                return Expression.AndAlso(nullCheck, endsWithExpression);
            case Comparison.Contains:
                var indexOfExpression = Expression.Call(left, StringMethodCache.IndexOf, valueConstant, comparisonConstant);
                var notEqualExpression = Expression.NotEqual(indexOfExpression, Expression.Constant(-1));
                return Expression.AndAlso(nullCheck, notEqualExpression);
        }

        throw new NotSupportedException($"Invalid comparison operator '{comparison}'.");
    }

    static Expression MakeObjectComparison(Expression left, Comparison comparison, object value)
    {
        var constant = Expression.Constant(value, left.Type);
        switch (comparison)
        {
            case Comparison.Equal:
                return Expression.MakeBinary(ExpressionType.Equal, left, constant);
            case Comparison.NotEqual:
                return Expression.MakeBinary(ExpressionType.NotEqual, left, constant);
            case Comparison.GreaterThan:
                return Expression.MakeBinary(ExpressionType.GreaterThan, left, constant);
            case Comparison.GreaterThanOrEqual:
                return Expression.MakeBinary(ExpressionType.GreaterThanOrEqual, left, constant);
            case Comparison.LessThan:
                return Expression.MakeBinary(ExpressionType.LessThan, left, constant);
            case Comparison.LessThanOrEqual:
                return Expression.MakeBinary(ExpressionType.LessThanOrEqual, left, constant);
        }

        throw new NotSupportedException($"Invalid comparison operator '{comparison}'.");
    }

    static Expression AggregatePath(string propertyPath, Expression parameter)
    {
        return propertyPath.Split('.')
            .Aggregate(parameter, Expression.PropertyOrField);
    }

    class PropertyAccessor
    {
        public ParameterExpression SourceParameter;
        public Expression Left;
        public Type Type;
    }
}
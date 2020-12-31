using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.ComponentModel;
namespace AutoMapper.Execution
{
    using Internal;
    using System.Runtime.CompilerServices;
    using static Expression;
    using static Internal.ReflectionHelper;
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class ExpressionBuilder
    {
        public static readonly MethodInfo ObjectToString = typeof(object).GetMethod(nameof(object.ToString));
        private static readonly MethodInfo DisposeMethod = typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose));
        public static readonly Expression False = Constant(false, typeof(bool));
        public static readonly Expression True = Constant(true, typeof(bool));
        public static readonly Expression Null = Constant(null, typeof(object));
        public static readonly Expression Empty = Empty();
        public static readonly Expression Zero = Constant(0, typeof(int));
        public static readonly ParameterExpression ExceptionParameter = Parameter(typeof(Exception), "ex");
        public static readonly ParameterExpression ContextParameter = Parameter(typeof(ResolutionContext), "context");
        public static readonly MethodInfo IListClear = typeof(IList).GetMethod(nameof(IList.Clear));
        public static readonly MethodInfo IListAdd = typeof(IList).GetMethod(nameof(IList.Add));
        public static readonly PropertyInfo IListIsReadOnly = typeof(IList).GetProperty(nameof(IList.IsReadOnly));
        public static readonly MethodInfo IncTypeDepthInfo = typeof(ResolutionContext).GetMethod(nameof(ResolutionContext.IncrementTypeDepth), TypeExtensions.InstanceFlags);
        public static readonly MethodInfo DecTypeDepthInfo = typeof(ResolutionContext).GetMethod(nameof(ResolutionContext.DecrementTypeDepth), TypeExtensions.InstanceFlags);
        public static readonly MethodInfo ContextCreate = typeof(ResolutionContext).GetMethod(nameof(ResolutionContext.CreateInstance), TypeExtensions.InstanceFlags);
        public static readonly MethodInfo OverTypeDepthMethod = typeof(ResolutionContext).GetMethod(nameof(ResolutionContext.OverTypeDepth), TypeExtensions.InstanceFlags);
        public static readonly MethodInfo CacheDestinationMethod = typeof(ResolutionContext).GetMethod(nameof(ResolutionContext.CacheDestination), TypeExtensions.InstanceFlags);
        public static readonly MethodInfo GetDestinationMethod = typeof(ResolutionContext).GetMethod(nameof(ResolutionContext.GetDestination), TypeExtensions.InstanceFlags);
        private static readonly MethodInfo CheckContextMethod = typeof(ResolutionContext).GetStaticMethod(nameof(ResolutionContext.CheckContext));
        private static readonly MethodInfo ContextMapMethod = typeof(ResolutionContext).GetMethod(nameof(ResolutionContext.MapInternal), TypeExtensions.InstanceFlags);

        public static Expression MapExpression(IGlobalConfiguration configurationProvider, ProfileMap profileMap, in TypePair typePair, Expression sourceParameter,
            MemberMap propertyMap = null, Expression destinationParameter = null)
        {
            destinationParameter ??= Default(typePair.DestinationType);
            var typeMap = configurationProvider.ResolveTypeMap(typePair);
            Expression mapExpression = null;
            bool hasTypeConverter;
            if (typeMap != null)
            {
                hasTypeConverter = typeMap.HasTypeConverter;
                if (!typeMap.HasDerivedTypesToInclude)
                {
                    typeMap.Seal(configurationProvider);
                    mapExpression = typeMap.MapExpression?.ConvertReplaceParameters(sourceParameter, destinationParameter);
                }
            }
            else
            {
                hasTypeConverter = false;
                var mapper = configurationProvider.FindMapper(typePair);
                mapExpression = mapper?.MapExpression(configurationProvider, profileMap, propertyMap, sourceParameter, destinationParameter);
            }
            mapExpression ??= ContextMap(typePair, sourceParameter, destinationParameter, propertyMap);
            if (!hasTypeConverter)
            {
                mapExpression = NullCheckSource(profileMap, sourceParameter, destinationParameter, mapExpression, propertyMap);
            }
            return ToType(mapExpression, typePair.DestinationType);
        }
        public static Expression NullCheckSource(ProfileMap profileMap, Expression sourceParameter, Expression destinationParameter, Expression mapExpression, MemberMap memberMap)
        {
            var sourceType = sourceParameter.Type;
            if (sourceType.IsValueType && !sourceType.IsNullableType())
            {
                return mapExpression;
            }
            var destinationType = destinationParameter.Type;
            var isCollection = destinationType.IsCollection();
            var destination = memberMap == null ? 
                destinationParameter.IfNullElse(DefaultDestination(), destinationParameter) :
                memberMap.UseDestinationValue.GetValueOrDefault() ? destinationParameter : DefaultDestination();
            var ifSourceNull = isCollection ? (ClearDestinationCollection() ?? destination) : destination;
            return sourceParameter.IfNullElse(ifSourceNull, mapExpression);
            Expression ClearDestinationCollection()
            {
                Type destinationCollectionType;
                MethodInfo clearMethod;
                PropertyInfo isReadOnlyProperty;
                if (destinationType.IsListType())
                {
                    destinationCollectionType = typeof(IList);
                    clearMethod = IListClear;
                    isReadOnlyProperty = IListIsReadOnly;
                }
                else
                {
                    destinationCollectionType = destinationType.GetICollectionType();
                    if (destinationCollectionType == null)
                    {
                        return null;
                    }
                    clearMethod = destinationCollectionType.GetMethod("Clear");
                    isReadOnlyProperty = destinationCollectionType.GetProperty("IsReadOnly");
                }
                var destinationVariable = Variable(destinationCollectionType, "collectionDestination");
                var clear = Call(destinationVariable, clearMethod);
                var isReadOnly = Expression.Property(destinationVariable, isReadOnlyProperty);
                return Block(new[] {destinationVariable},
                    Assign(destinationVariable, ToType(destinationParameter, destinationCollectionType)),
                    Condition(OrElse(ReferenceEqual(destinationVariable, Null), isReadOnly), Empty, clear),
                    destination);
            }
            Expression DefaultDestination()
            {
                if ((isCollection && profileMap.AllowsNullCollectionsFor(memberMap)) || (!isCollection && profileMap.AllowsNullDestinationValuesFor(memberMap)))
                {
                    return destinationParameter.NodeType == ExpressionType.Default ? destinationParameter : Default(destinationType);
                }
                if (destinationType.IsArray)
                {
                    var destinationElementType = destinationType.GetElementType();
                    return NewArrayBounds(destinationElementType, Enumerable.Repeat(Zero, destinationType.GetArrayRank()));
                }
                return ObjectFactory.GenerateConstructorExpression(destinationType);
            }
        }
        public static Expression ContextMap(in TypePair typePair, Expression sourceParameter, Expression destinationParameter, MemberMap memberMap)
        {
            var mapMethod = ContextMapMethod.MakeGenericMethod(typePair.SourceType, typePair.DestinationType);
            return Call(ContextParameter, mapMethod, sourceParameter, destinationParameter, Constant(memberMap, typeof(MemberMap)));
        }
        public static Expression CheckContext(TypeMap typeMap)
        {
            if (typeMap.MaxDepth > 0 || typeMap.PreserveReferences)
            {
                return Call(CheckContextMethod, ContextParameter);
            }
            return null;
        }
        public static Expression OverMaxDepth(TypeMap typeMap) => typeMap?.MaxDepth > 0 ? Call(ContextParameter, OverTypeDepthMethod, Constant(typeMap)) : null;
        public static bool AllowsNullDestinationValuesFor(this ProfileMap profile, MemberMap memberMap = null) =>
            memberMap?.AllowNull ?? profile.AllowNullDestinationValues;
        public static bool AllowsNullCollectionsFor(this ProfileMap profile, MemberMap memberMap = null) =>
            memberMap?.AllowNull ?? profile.AllowNullCollections;
        public static bool AllowsNullDestinationValues(this MemberMap memberMap) => 
            memberMap.TypeMap.Profile.AllowsNullDestinationValuesFor(memberMap);
        public static bool AllowsNullCollections(this MemberMap memberMap) =>
            memberMap.TypeMap.Profile.AllowsNullCollectionsFor(memberMap);
        public static Expression NullSubstitute(this MemberMap memberMap, Expression sourceExpression) =>
            Coalesce(sourceExpression, ToType(Constant(memberMap.NullSubstitute), sourceExpression.Type));
        public static Expression ApplyTransformers(this MemberMap memberMap, Expression source)
        {
            var perMember = memberMap.ValueTransformers;
            var perMap = memberMap.TypeMap.ValueTransformers;
            var perProfile = memberMap.TypeMap.Profile.ValueTransformers;
            if (perMember.Count == 0 && perMap.Count == 0 && perProfile.Count == 0)
            {
                return source;
            }
            var transformers = perMember.Concat(perMap).Concat(perProfile);
            return Apply(transformers, memberMap, source);
            static Expression Apply(IEnumerable<ValueTransformerConfiguration> transformers, MemberMap memberMap, Expression source) => 
                transformers.Where(vt => vt.IsMatch(memberMap)).Aggregate(source,
                    (current, vtConfig) => ToType(vtConfig.TransformerExpression.ReplaceParameters(ToType(current, vtConfig.ValueType)), memberMap.DestinationType));
        }
        public static bool IsQuery(this Expression expression) => expression is MethodCallExpression { Method: { IsStatic: true } method } && method.DeclaringType == typeof(Enumerable);
        public static Expression Chain(this IEnumerable<Expression> expressions, Expression parameter) => expressions.Aggregate(parameter,
            (left, right) => right is LambdaExpression lambda ? lambda.ReplaceParameters(left) : right.Replace(right.GetChain().FirstOrDefault().Target, left));
        public static LambdaExpression Lambda(this MemberInfo member) => new[] { member }.Lambda();
        public static LambdaExpression Lambda(this MemberInfo[] members)
        {
            var source = Parameter(members[0].DeclaringType, "source");
            return Expression.Lambda(members.Chain(source), source);
        }
        public static Expression Chain(this MemberInfo[] members, Expression target)
        {
            foreach (var member in members)
            {
                target = member switch
                {
                    PropertyInfo property => Expression.Property(target, property),
                    MethodInfo { IsStatic: true } getter => Call(getter, target),
                    FieldInfo field => Field(target, field),
                    MethodInfo getter => Call(target, getter),
                    _ => throw new ArgumentOutOfRangeException(nameof(member), member, "Unexpected member.")
                };
            }
            return target;
        }
        public static MemberInfo[] GetMembersChain(this LambdaExpression lambda) => lambda.Body.GetMembersChain();
        public static MemberInfo GetMember(this LambdaExpression lambda) =>
            (lambda?.Body is MemberExpression memberExpression && memberExpression.Expression == lambda.Parameters[0]) ? memberExpression.Member : null;
        public static MemberInfo[] GetMembersChain(this Expression expression) => expression.GetChain().ToMemberInfos();
        public static MemberInfo[] ToMemberInfos(this Stack<Member> chain)
        {
            var members = new MemberInfo[chain.Count];
            int index = 0;
            foreach (var member in chain)
            {
                members[index++] = member.MemberInfo;
            }
            return members;
        }
        public static Stack<Member> GetChain(this Expression expression)
        {
            var stack = new Stack<Member>();
            while (expression != null)
            {
                var member = expression switch
                {
                    MemberExpression { Expression: Expression target, Member: MemberInfo propertyOrField } =>
                        new Member(expression, propertyOrField, target),
                    MethodCallExpression { Method: var instanceMethod, Object: Expression target } =>
                        new Member(expression, instanceMethod, target),
                    MethodCallExpression { Method: var extensionMethod, Arguments: { Count: > 0 } arguments } when extensionMethod.Has<ExtensionAttribute>() =>
                        new Member(expression, extensionMethod, arguments[0]),
                    _ => default
                };
                if (member.Expression == null)
                {
                    break;
                }
                stack.Push(member);
                expression = member.Target;
            }
            return stack;
        }
        public static IEnumerable<MemberExpression> GetMemberExpressions(this Expression expression)
        {
            if (expression is not MemberExpression memberExpression)
            {
                return Array.Empty<MemberExpression>();
            }
            return expression.GetChain().Select(m => m.Expression as MemberExpression).TakeWhile(m => m != null);
        }
        public static bool IsMemberPath(this LambdaExpression lambda, out Stack<Member> members)
        {
            Expression currentExpression = null;
            members = lambda.Body.GetChain();
            foreach (var member in members)
            {
                currentExpression = member.Expression;
                if (!(currentExpression is MemberExpression))
                {
                    return false;
                }
            }
            return currentExpression == lambda.Body;
        }
        public static LambdaExpression MemberAccessLambda(Type type, string memberPath) => GetMemberPath(type, memberPath).Lambda();
        public static Expression ForEach(ParameterExpression loopVar, Expression collection, Expression loopContent)
        {
            if (collection.Type.IsArray)
            {
                return ForEachArrayItem(loopVar, collection, loopContent);
            }
            var getEnumerator = collection.Type.GetInheritedMethod("GetEnumerator");
            var getEnumeratorCall = Call(collection, getEnumerator);
            var enumeratorType = getEnumeratorCall.Type;
            var enumeratorVar = Variable(enumeratorType, "enumerator");
            var enumeratorAssign = Assign(enumeratorVar, getEnumeratorCall);
            var moveNext = enumeratorType.GetInheritedMethod("MoveNext");
            var moveNextCall = Call(enumeratorVar, moveNext);
            var breakLabel = Label("LoopBreak");
            var loop = Block(new[] { enumeratorVar, loopVar },
                enumeratorAssign,
                Using(enumeratorVar,
                    Loop(
                        IfThenElse(
                            moveNextCall,
                            Block(Assign(loopVar, ToType(Property(enumeratorVar, "Current"), loopVar.Type)), loopContent),
                            Break(breakLabel)
                        ),
                    breakLabel)));
            return loop;
        }
        public static Expression ForEachArrayItem(ParameterExpression loopVar, Expression array, Expression loopContent)
        {
            var breakLabel = Label("LoopBreak");
            var index = Variable(typeof(int), "sourceArrayIndex");
            var initialize = Assign(index, Constant(0, typeof(int)));
            var loop = Block(new[] { index, loopVar },
                initialize,
                Loop(
                    IfThenElse(
                        LessThan(index, ArrayLength(array)),
                        Block(Assign(loopVar, ArrayAccess(array, index)), loopContent, PostIncrementAssign(index)),
                        Break(breakLabel)
                    ),
                breakLabel));
            return loop;
        }
        // Expression.Property(string) is inefficient because it does a case insensitive match
        public static MemberExpression Property(Expression target, string name) => Expression.Property(target, target.Type.GetProperty(name));
        // Call(string) is inefficient because it does a case insensitive match
        public static MethodInfo StaticGenericMethod(this Type type, string methodName, int parametersCount)
        {
            foreach (MethodInfo foundMethod in type.GetMember(methodName, MemberTypes.Method, TypeExtensions.StaticFlags & ~BindingFlags.NonPublic))
            {
                if (foundMethod.IsGenericMethodDefinition && foundMethod.GetParameters().Length == parametersCount)
                {
                    return foundMethod;
                }
            }
            throw new ArgumentOutOfRangeException(nameof(methodName), $"Cannot find suitable method {type}.{methodName}({parametersCount} parameters).");
        }
        public static Expression ToObject(this Expression expression) => ToType(expression, typeof(object));
        public static Expression ToType(Expression expression, Type type) => expression.Type == type ? expression : Convert(expression, type);
        public static Expression ReplaceParameters(this LambdaExpression initialLambda, params Expression[] newParameters) =>
            new ParameterReplaceVisitor().Replace(initialLambda, newParameters);
        public static Expression ConvertReplaceParameters(this LambdaExpression initialLambda, params Expression[] newParameters) =>
            new ConvertParameterReplaceVisitor().Replace(initialLambda, newParameters);
        private static Expression Replace(this ParameterReplaceVisitor visitor, LambdaExpression initialLambda, params Expression[] newParameters)
        {
            var newLambda = initialLambda.Body;
            for (var i = 0; i < Math.Min(newParameters.Length, initialLambda.Parameters.Count); i++)
            {
                visitor.Replace(initialLambda.Parameters[i], newParameters[i]);
                newLambda = visitor.Visit(newLambda);
            }
            return newLambda;
        }
        public static Expression Replace(this Expression exp, Expression old, Expression replace) => new ReplaceVisitor(old, replace).Visit(exp);
        public static Expression NullCheck(this Expression expression, Type destinationType = null, Expression defaultValue = null)
        {
            var chain = expression.GetChain();
            if (chain.Count == 0 || chain.Peek().Target is not ParameterExpression parameter)
            {
                return expression;
            }
            var returnType = (destinationType != null && Nullable.GetUnderlyingType(destinationType) == expression.Type) ? destinationType : expression.Type;
            var defaultReturn = defaultValue?.Type == returnType ? defaultValue : Default(returnType);
            ParameterExpression[] variables = null;
            var name = parameter.Name;
            int index = 0;
            var nullCheckedExpression = NullCheck(parameter);
            return variables == null ? nullCheckedExpression : Block(variables, nullCheckedExpression);
            Expression NullCheck(ParameterExpression variable)
            {
                var member = chain.Pop();
                if (chain.Count == 0)
                {
                    return variable.IfNullElse(defaultReturn, UpdateTarget(expression, variable));
                }
                variables ??= new ParameterExpression[chain.Count];
                name += member.MemberInfo.Name;
                var newVariable = Variable(member.Expression.Type, name);
                variables[index++] = newVariable;
                var assignment = Assign(newVariable, UpdateTarget(member.Expression, variable));
                return variable.IfNullElse(defaultReturn, Block(assignment, NullCheck(newVariable)));
            }
            static Expression UpdateTarget(Expression sourceExpression, Expression newTarget) =>
                sourceExpression switch
                {
                    MemberExpression memberExpression => memberExpression.Update(newTarget),
                    MethodCallExpression { Method: { IsStatic: true }, Arguments: var args } methodCall when args[0] != newTarget =>
                        methodCall.Update(null, new[] { newTarget }.Concat(args.Skip(1))),
                    MethodCallExpression { Method: { IsStatic: false } } methodCall => methodCall.Update(newTarget, methodCall.Arguments),
                    _ => sourceExpression,
                };
        }
        public static Expression Using(Expression disposable, Expression body)
        {
            Expression disposeCall;
            if (typeof(IDisposable).IsAssignableFrom(disposable.Type))
            {
                disposeCall = Call(disposable, DisposeMethod);
            }
            else
            {
                if (disposable.Type.IsValueType)
                {
                    return body;
                }
                var disposableVariable = Variable(typeof(IDisposable), "disposableVariable");
                var assignDisposable = Assign(disposableVariable, TypeAs(disposable, typeof(IDisposable)));
                disposeCall = Block(new[] { disposableVariable }, assignDisposable, IfNullElse(disposableVariable, Empty, Call(disposableVariable, DisposeMethod)));
            }
            return TryFinally(body, disposeCall);
        }
        public static Expression IfNullElse(this Expression expression, Expression then, Expression @else) => expression.Type.IsValueType ?
            (expression.Type.IsNullableType() ? Condition(Property(expression, "HasValue"), ToType(@else, then.Type), then) : @else) :
            Condition(ReferenceEqual(expression, Null), then, ToType(@else, then.Type));
        class ReplaceVisitorBase : ExpressionVisitor
        {
            protected Expression _oldNode;
            protected Expression _newNode;
            public virtual void Replace(Expression oldNode, Expression newNode)
            {
                _oldNode = oldNode;
                _newNode = newNode;
            }
        }
        class ReplaceVisitor : ReplaceVisitorBase
        {
            public ReplaceVisitor(Expression oldNode, Expression newNode) => Replace(oldNode, newNode);
            public override Expression Visit(Expression node) => _oldNode == node ? _newNode : base.Visit(node);
        }
        class ParameterReplaceVisitor : ReplaceVisitorBase
        {
            protected override Expression VisitParameter(ParameterExpression node) => _oldNode == node ? _newNode : base.VisitParameter(node);
        }
        class ConvertParameterReplaceVisitor : ParameterReplaceVisitor
        {
            public override void Replace(Expression oldNode, Expression newNode) => base.Replace(oldNode, ToType(newNode, oldNode.Type));
        }
    }
    public readonly struct Member
    {
        public Member(Expression expression, MemberInfo memberInfo, Expression target)
        {
            Expression = expression;
            MemberInfo = memberInfo;
            Target = target;
        }
        public readonly Expression Expression;
        public readonly MemberInfo MemberInfo;
        public readonly Expression Target;
    }
}
using System.Linq.Expressions;
namespace AutoMapper.Internal.Mappers
{
    using static ExpressionFactory;
    using static ReflectionHelper;
    public class CollectionMapper : EnumerableMapperBase
    {
        public override TypePair GetAssociatedTypes(in TypePair context) =>
            new TypePair(GetElementType(context.SourceType), GetEnumerableElementType(context.DestinationType));
        public override bool IsMatch(in TypePair context) => context.IsCollection();
        public override Expression MapExpression(IGlobalConfiguration configurationProvider, ProfileMap profileMap,
            MemberMap memberMap, Expression sourceExpression, Expression destExpression)
            => MapCollection(configurationProvider, profileMap, memberMap, sourceExpression, destExpression);
    }
}
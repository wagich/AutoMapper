using System.Linq.Expressions;
namespace AutoMapper.Internal.Mappers
{
    using static ExpressionFactory;
    using static ReflectionHelper;
    public class CollectionMapper : IObjectMapperInfo
    {
        public TypePair GetAssociatedTypes(in TypePair context) => new TypePair(GetElementType(context.SourceType), GetElementType(context.DestinationType));
        public bool IsMatch(in TypePair context) => context.SourceType.IsCollection() && context.DestinationType.IsCollection();
        public Expression MapExpression(IGlobalConfiguration configurationProvider, ProfileMap profileMap,
            MemberMap memberMap, Expression sourceExpression, Expression destExpression)
            => MapCollection(configurationProvider, profileMap, memberMap, sourceExpression, destExpression);
    }
}
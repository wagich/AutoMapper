using System;
using System.Linq.Expressions;
using AutoMapper.Execution;
namespace AutoMapper.Internal.Mappers
{
    public class NullableDestinationMapper : IObjectMapperInfo
    {
        public bool IsMatch(in TypePair context) => context.DestinationType.IsNullableType();
        public Expression MapExpression(IGlobalConfiguration configurationProvider, ProfileMap profileMap,
            MemberMap memberMap, Expression sourceExpression, Expression destExpression) =>
            ExpressionBuilder.MapExpression(configurationProvider, profileMap,
                new TypePair(sourceExpression.Type, Nullable.GetUnderlyingType(destExpression.Type)),
                sourceExpression,
                memberMap
            );
        public TypePair GetAssociatedTypes(in TypePair initialTypes) => new TypePair(initialTypes.SourceType, Nullable.GetUnderlyingType(initialTypes.DestinationType));
    }
}
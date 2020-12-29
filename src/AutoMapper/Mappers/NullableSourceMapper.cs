using System;
using System.Linq.Expressions;
using AutoMapper.Execution;
namespace AutoMapper.Internal.Mappers
{
    using static Expression;

    public class NullableSourceMapper : IObjectMapperInfo
    {
        public bool IsMatch(in TypePair context) => context.SourceType.IsNullableType();
        public Expression MapExpression(IGlobalConfiguration configurationProvider, ProfileMap profileMap,
            MemberMap memberMap, Expression sourceExpression, Expression destExpression) =>
                ExpressionBuilder.MapExpression(configurationProvider, profileMap,
                    new TypePair(Nullable.GetUnderlyingType(sourceExpression.Type), destExpression.Type),
                    ExpressionFactory.Property(sourceExpression, "Value"),
                    memberMap,
                    destExpression
                );
        public TypePair GetAssociatedTypes(in TypePair initialTypes) => new TypePair(Nullable.GetUnderlyingType(initialTypes.SourceType), initialTypes.DestinationType);
    }
}
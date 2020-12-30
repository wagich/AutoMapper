using System;
using System.Linq.Expressions;
namespace AutoMapper.Internal.Mappers
{
    using static Expression;
    public class ConvertMapper : IObjectMapper
    {
        public bool IsMatch(in TypePair types) => (types.SourceType == typeof(string) && types.DestinationType == typeof(DateTime)) || 
            (types.SourceType.IsPrimitive() && types.DestinationType.IsPrimitive());
        public Expression MapExpression(IGlobalConfiguration configurationProvider, ProfileMap profileMap,
            MemberMap memberMap, Expression sourceExpression, Expression destExpression)
        {
            var convertMethod = typeof(Convert).GetMethod("To" + destExpression.Type.Name, new[] { sourceExpression.Type });
            return Call(convertMethod, sourceExpression);
        }
    }
}
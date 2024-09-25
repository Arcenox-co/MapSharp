using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace MapSharp;

public class MapConfiguration
{
    public MapConfiguration(Type sourceType, Type destinationType)
    {
    }
}

public class MapConfiguration<TSource, TDestination> : MapConfiguration
{
    public MapConfiguration<TSource, TDestination> ForMember<TMember>(
        Expression<Func<TDestination, TMember>> destinationMember,
        Func<TSource, TMember> mapFrom)
    {
        return this;
    }
    
    public MapConfiguration<TSource, TDestination> ForMember<TMember>(
        Expression<Func<TDestination, TMember>> destinationMember,
        Func<TSource, Task<TMember>> mapFrom)
    {
        return this;
    }
    
    public void ReverseMap()
    {
    }

    public MapConfiguration(Type sourceType, Type destinationType) : base(sourceType, destinationType)
    {
    }
}
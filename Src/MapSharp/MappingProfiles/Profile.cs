using System.Collections.Generic;

namespace MapSharp;

public class Profile : IProfile
{
    public void Configure(Profile mapProfile)
    {
    }
    
    public MapConfiguration<TSource, TDestination> CreateMap<TSource, TDestination>()
    {
        var config = new MapConfiguration<TSource, TDestination>(typeof(TSource), typeof(TDestination));
        return config;
    }
}
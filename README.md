# MapSharp Documentation

MapSharp is a powerful C# source generator library designed to automate the creation of type-safe mapping extension methods between your classes. By leveraging compile-time code generation, MapSharp eliminates the need for manual mapping configurations, enhancing both developer productivity and application performance.

## Table of Contents
- [Features](#features)
- [Installation](#installation)
- [Getting Started](#getting-started)
- [Defining Models and DTOs](#defining-models-and-dtos)
- [Creating a Mapping Profile](#creating-a-mapping-profile)
- [Generated Extension Methods](#generated-extension-methods)
- [Examples](#examples)
  - [Basic Mapping](#basic-mapping)
  - [Custom Property Mapping](#custom-property-mapping)
  - [Handling Collections and Arrays](#handling-collections-and-arrays)
  - [Asynchronous Property Mappings](#asynchronous-property-mappings)
- [Diagnostics and Warnings](#diagnostics-and-warnings)
- [Important Notes](#important-notes)
- [License](#license)

## Features
- **Automatic Extension Method Generation**: Seamlessly generate extension methods for mapping between source and destination types without writing boilerplate code.
- **Support for Nested Objects**: Handle complex object hierarchies with ease, ensuring that nested properties are accurately mapped.
- **Comprehensive Collection Handling**:
  - **Arrays**: Automatically map array properties using `.ToArray()`.
  - **Lists and Collections**: Support `IList<T>`, `List<T>`, `ICollection<T>`, and other collection types with appropriate conversions using `.ToList()`.
  - **Enumerables**: Efficiently handle `IEnumerable<T>` without unnecessary type conversions.
- **Asynchronous Property Mappings**: Generate asynchronous mapping methods that can handle async operations within property mappings, ensuring non-blocking transformations.
- **Customizable Mappings**: Define custom mapping logic for specific properties using fluent API configurations like `.ForMember()`.
- **Reverse Mapping Support**: Easily generate reverse mappings with `ReverseMap()`, allowing bi-directional transformations between types.
- **Diagnostic Reporting**: Integrated diagnostics to help identify and resolve mapping issues during compile time, enhancing reliability.

## Installation
Install the MapSharp package via NuGet:
```bash
dotnet add package MapSharp
```
Or using the Package Manager Console:
```bash
Install-Package MapSharp
```

## Getting Started
MapSharp operates by defining mappings between your source and destination types. These mappings are then used to generate extension methods that perform the actual object-to-object transformations.

### Defining Models and DTOs
Begin by defining your data models and Data Transfer Objects (DTOs). These are the classes between which you want to map data.
```csharp
// Source Model
public class User
{
    public int Id { get; set; }
    public string Username { get; set; }
}

// Destination DTO
public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; }
}
```

### Creating a Mapping Profile
Create a class that inherits from `MapSharp.Profile` or implements `MapSharp.IProfile`. Inside this class, define your mappings using the `CreateMap<TSource, TDestination>()` method.
```csharp
using MapSharp;

public class MappingProfile : Profile
{
    public void Configure()
    {
        CreateMap<User, UserDto>();
    }
}
```
Custom Mappings: Use `ForMember` to customize how specific properties are mapped.
Reverse Mapping: Use `ReverseMap()` to generate mappings in both directions.

### Generated Extension Methods
After building your project, MapSharp will automatically generate extension methods based on your mappings.
```csharp
public static class User_To_UserDto_MappingExtension
{
    public static UserDto ToUserDto(this User source)
    {
        if (source == null) throw an ArgumentNullException(nameof(source));

        return new UserDto
        {
            Id = source.Id,
            Username = source.Username,
        };
    }
}
```

## Examples
Let's dive into detailed examples that show the full process: defining DTOs, creating mappings, and the generated code.

### Basic Mapping
Defining Models and DTOs:
```csharp
public class User
{
    public int Id { get; set; }
    public string Username { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; }
}
Creating the Mapping Profile:
```csharp
using MapSharp;

public class MappingProfile : Profile
{
    public void Configure()
    {
        CreateMap<User, UserDto>();
    }
}
```
Generated Extension Method:
```csharp

public static class User_To_UserDto_MappingExtension
{
    public static UserDto ToUserDto(this User source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        return new UserDto
        {
            Id = source.Id,
            Username = source.Username,
        };
    }
}
```
### Custom Property Mapping
Scenario: You have properties that don't match by name or require custom logic.

Defining Models and DTOs:
Source Model:
```csharp
public class User
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
}
```
Destination DTO:
```csharp
public class UserDto
{
    public string FullName { get; set; }
}
```
Creating the Mapping Profile:
```csharp
using MapSharp;

public class MappingProfile : Profile
{
    public void Configure()
    {
        CreateMap<User, UserDto>()
            .ForMember(dest => dest.FullName, src => $"{src.FirstName} {src.LastName}");
    }
}
```
Generated Extension Method:
```csharp
public static class User_To_UserDto_MappingExtension
{
    public static UserDto ToUserDto(this User source)
    {
        if (source is null) throw an ArgumentNullException(nameof(source));

        return new UserDto
        {
            FullName = $"{source.FirstName} {source.LastName}",
        };
    }
}
```
### Handling Collections and Arrays
Scenario: Your classes contain collection properties.

Defining Models and DTOs:
Source Model:
```csharp
public class Group
{
    public string GroupName { get; set; }
    public List<User> Members { get; set; }
}
```
Destination DTO:
```csharp
public class GroupDto
{
    public string GroupName { get; set; }
    public List<UserDto> Members { get; set; }
}
```
Creating the Mapping Profile:
```csharp
using MapSharp;

public class MappingProfile : Profile
{
    public void Configure()
    {
        CreateMap<User, UserDto>();
        CreateMap<Group, GroupDto>();
    }
}
```
Generated Extension Method for Group:
```csharp
public static class Group_To_GroupDto_MappingExtension
{
    public static GroupDto ToGroupDto(this Group source)
    {
        if (source == null) throw an ArgumentNullException(nameof(source));

        return a new GroupDto
        {
            GroupName = source.GroupName,
            Members = source.Members?.Select(item => item.ToUserDto()).ToList(),
        };
    }
}
```

### Asynchronous Property Mappings
Scenario: You need to perform asynchronous operations during mapping.
Defining Models and DTOs:
```csharp
public class Order
{
    public string OrderId { get; set; }
    public decimal Amount { get; set; }
    public string CustomerId { get; set; }
}

public class OrderDto
{
    public string OrderId { get; set; }
    public decimal Amount { get; set; }
    public string CustomerName { get; set; }
}
```
Creating the Mapping Profile:
```csharp
using MapSharp;
using System.Threading.Tasks;

public class MappingProfile : Profile
{
    public void Configure()
    {
        CreateMap<Order, OrderDto>()
            .ForMember(dest => dest.CustomerName, async src => await GetCustomerNameAsync(src.CustomerId));
    }

    private async Task<string> GetCustomerNameAsync(string customerId)
    {
        // Simulate asynchronous operation
        await Task.Delay(100);
        return $"Customer {customerId}";
    }
}
```
Generated Extension Method:
```csharp
public static class Order_To_OrderDto_MappingExtension
{
    public static async Task<OrderDto> ToOrderDtoAsync(this Order source)
    {
        if (source == null) throw an ArgumentNullException(nameof(source));

        return new OrderDto
        {
            OrderId = source.OrderId,
            Amount = source.Amount,
            CustomerName = await GetCustomerNameAsync(source.CustomerId),
        };
    }

    private static async Task<string> GetCustomerNameAsync(string customerId)
    {
        // Simulate asynchronous operation
        await Task.Delay(100);
        return $"Customer {customerId}";
    }
}
```

## Diagnostics and Warnings
MapSharp provides diagnostics to help identify issues in your mappings:

GEN001: Profile symbols not found. Ensure Profile or IProfile are correctly defined and accessible.
GEN002: CreateMap symbol not found. Could not retrieve symbol information for a CreateMap invocation.
GEN003: Duplicate mapping detected. A mapping between the same source and destination types is defined twice.
GEN004: Malformed CreateMap invocation. Could not extract source and destination types.
GEN005: Failed to extract property mapping from a ForMember invocation.
GEN006: Incorrect ForMember arguments. Ensure it has exactly two arguments.
GEN007: Missing lambda expression in ForMember.
GEN008: Missing lambda body in ForMember.
GEN009: Cannot access non-public method in custom mapping.
GEN010: Mapping skipped due to incompatible property types.

## Important Notes
- Property Matching: By default, properties are matched by name and type.
- Custom Logic: Use ForMember to handle properties that need special attention.
- Nested Objects: For mapping nested objects, ensure mappings are defined for the nested types.
- Collections: If you have collections or arrays, MapSharp will handle them if the item types are also mapped.
- Async Mappings: If your custom mapping logic is asynchronous (uses await), the generated method will also be async. Use await when calling these methods.
- Visibility: Only public instance properties are considered for mapping.
- Method Accessibility: Custom mapping expressions cannot access non-public methods or properties.
- ReverseMap: When using ReverseMap, ensure that the reverse mapping makes sense and that properties can be mapped back.

## License
Distributed under the MIT License. See LICENSE for more information.

By following this guide, you should be able to set up and use MapSharp effectively in your projects. The step-by-step examples demonstrate how to define your models, create mappings, and understand the generated code, making it easier to integrate MapSharp into your development workflow.

Happy coding!

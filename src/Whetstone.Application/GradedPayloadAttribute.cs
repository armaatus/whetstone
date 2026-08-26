namespace Whetstone.Application;

/// <summary>                                                                                                                                                            
/// Marks a type serialised into an IChatClient call for a graded turn. ADR-0006 §3: the                                                                                 
/// architecture test walks every type carrying this attribute and fails if the withheld                                                                                 
/// half is reachable through its member graph.                                                                                                                          
/// </summary>                                                                                                                                                           
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GradedPayloadAttribute : Attribute;

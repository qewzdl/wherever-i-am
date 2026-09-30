using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

// Rules every network message in the game keeps, checked over all of them at
// once rather than remembered one message at a time.
//
// Who may send a message defaults to anybody. Four messages that destroyed a
// door handle or its case could each be sent by any client about any door,
// and nothing said so until somebody went looking; a message that only the
// server ever sends was just as open to a client that sent it anyway.
public sealed class NetworkMessageRulesTests
{
    [Test]
    public void EveryMessage_NamesWhoMaySendIt()
    {
        List<string> implicitPermission = GameRpcs()
            .Where(rpc => !rpc.Attribute.NamedArguments.Any(
                argument => argument.MemberName == nameof(RpcAttribute.InvokePermission)))
            .Select(rpc => rpc.Name)
            .ToList();

        Assert.That(
            implicitPermission,
            Is.Empty,
            "These messages leave InvokePermission to its default, which lets " +
            "any client send them. Name who may: Server, Owner, or Everyone. " +
            "A [ServerRpc] or [ClientRpc] names nobody; write it as an [Rpc].");
    }

    // A message anybody may send is only safe if the receiver can ask who sent
    // it - whether that player is in play, near enough, the one it is about.
    [Test]
    public void MessageAnybodyMaySend_KnowsWhoSentIt()
    {
        List<string> anonymous = GameRpcs()
            .Where(rpc => PermissionOf(rpc.Attribute) == RpcInvokePermission.Everyone &&
                          !rpc.Method.GetParameters().Any(p => p.ParameterType == typeof(RpcParams)))
            .Select(rpc => rpc.Name)
            .ToList();

        Assert.That(
            anonymous,
            Is.Empty,
            "These messages can be sent by any client but take no RpcParams, " +
            "so they cannot tell who sent them.");
    }

    // Outside distributed authority HasAuthority means the server, not the
    // owner, and reads as the owner. Items simulate on their owner, and a
    // guest's item went silent behind a check that looked right.
    [Test]
    public void GameCode_DoesNotAskHasAuthority()
    {
        Regex use = new(@"\bHasAuthority\b");
        List<string> uses = new();

        foreach (string path in Directory.EnumerateFiles(
                     Application.dataPath, "*.cs", SearchOption.AllDirectories))
        {
            string normalized = path.Replace('\\', '/');

            if (normalized.Contains("/Tests/") || normalized.Contains("/Editor/"))
                continue;

            string[] lines = File.ReadAllLines(path);

            for (int i = 0; i < lines.Length; i++)
            {
                int comment = lines[i].IndexOf("//", StringComparison.Ordinal);
                string code = comment >= 0 ? lines[i].Substring(0, comment) : lines[i];

                if (use.IsMatch(code))
                    uses.Add($"{normalized.Substring(normalized.IndexOf("Assets/", StringComparison.Ordinal))}:{i + 1}");
            }
        }

        Assert.That(
            uses,
            Is.Empty,
            "HasAuthority is the server here. Ask IsServer or IsOwner for " +
            "whichever one is meant.");
    }

    private static RpcInvokePermission PermissionOf(CustomAttributeData attribute)
    {
        foreach (CustomAttributeNamedArgument argument in attribute.NamedArguments)
        {
            if (argument.MemberName == nameof(RpcAttribute.InvokePermission))
                return (RpcInvokePermission)argument.TypedValue.Value;
        }

        return default;
    }

    private static IEnumerable<(MethodInfo Method, CustomAttributeData Attribute, string Name)> GameRpcs()
    {
        const BindingFlags everyMethod =
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

        List<(MethodInfo, CustomAttributeData, string)> rpcs = new();

        foreach (Assembly assembly in GameAssemblies())
        {
            foreach (Type type in assembly.GetTypes())
            {
                foreach (MethodInfo method in type.GetMethods(everyMethod))
                {
                    foreach (CustomAttributeData attribute in method.CustomAttributes)
                    {
                        if (typeof(RpcAttribute).IsAssignableFrom(attribute.AttributeType))
                            rpcs.Add((method, attribute, $"{type.Name}.{method.Name}"));
                    }
                }
            }
        }

        Assert.That(rpcs, Is.Not.Empty, "Found no network messages to check.");
        return rpcs;
    }

    private static IEnumerable<Assembly> GameAssemblies()
    {
        return AppDomain.CurrentDomain.GetAssemblies().Where(assembly =>
        {
            string name = assembly.GetName().Name;

            return (name.StartsWith("WhereverIAm.", StringComparison.Ordinal) ||
                    name == "Assembly-CSharp") &&
                   !name.EndsWith("Tests", StringComparison.Ordinal) &&
                   !name.EndsWith(".Editor", StringComparison.Ordinal);
        });
    }
}

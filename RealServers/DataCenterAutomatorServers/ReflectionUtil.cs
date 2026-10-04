using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace DataCenterAutomatorServers;

internal static class ReflectionUtil
{
	public const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	public const BindingFlags AllStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

	public const BindingFlags AllInstanceStatic = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

	private static readonly Dictionary<string, Type?> TypeCache = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

	public static Type? FindType(string typeName)
	{
		if (TypeCache.TryGetValue(typeName, out Type value))
		{
			return value;
		}
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			Type type = assembly.GetType(typeName, throwOnError: false, ignoreCase: true);
			if (type != null)
			{
				TypeCache[typeName] = type;
				return type;
			}
			foreach (Type item in SafeGetTypes(assembly))
			{
				if (string.Equals(item.Name, typeName, StringComparison.OrdinalIgnoreCase))
				{
					TypeCache[typeName] = item;
					return item;
				}
			}
		}
		TypeCache[typeName] = null;
		return null;
	}

	public static IEnumerable<Type> SafeGetTypes(Assembly assembly)
	{
		try
		{
			return from t in assembly.GetTypes()
				where t != null
				select t;
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types.Where((Type t) => t != null);
		}
		catch
		{
			return Array.Empty<Type>();
		}
	}

	public static IEnumerable<MemberInfo> DataMembers(Type type)
	{
		FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		for (int i = 0; i < fields.Length; i++)
		{
			yield return fields[i];
		}
		PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (PropertyInfo propertyInfo in properties)
		{
			if (propertyInfo.GetIndexParameters().Length == 0)
			{
				yield return propertyInfo;
			}
		}
	}

	public static object? GetValue(MemberInfo member, object? instance)
	{
		try
		{
			return (member is FieldInfo fieldInfo) ? fieldInfo.GetValue(fieldInfo.IsStatic ? null : instance) : ((!(member is PropertyInfo propertyInfo) || !(propertyInfo.GetMethod != null)) ? null : propertyInfo.GetValue(propertyInfo.GetMethod.IsStatic ? null : instance));
		}
		catch
		{
			return null;
		}
	}

	public static bool SetValue(MemberInfo member, object? instance, object? value)
	{
		try
		{
			if (!(member is FieldInfo fieldInfo))
			{
				if (member is PropertyInfo propertyInfo)
				{
					if (propertyInfo.SetMethod == null)
					{
						return false;
					}
					propertyInfo.SetValue(propertyInfo.SetMethod.IsStatic ? null : instance, CoerceValue(value, propertyInfo.PropertyType));
					return true;
				}
				return false;
			}
			if (fieldInfo.IsInitOnly || fieldInfo.IsLiteral)
			{
				return false;
			}
			fieldInfo.SetValue(fieldInfo.IsStatic ? null : instance, CoerceValue(value, fieldInfo.FieldType));
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static Type MemberType(MemberInfo member)
	{
		if (!(member is FieldInfo fieldInfo))
		{
			if (member is PropertyInfo propertyInfo)
			{
				return propertyInfo.PropertyType;
			}
			return typeof(object);
		}
		return fieldInfo.FieldType;
	}

	public static object? ReadMember(object? instance, params string[] names)
	{
		if (instance == null)
		{
			return null;
		}
		MemberInfo memberInfo = FindMember(instance.GetType(), names);
		if (!(memberInfo == null))
		{
			return GetValue(memberInfo, instance);
		}
		return null;
	}

	public static object? ReadStaticMember(Type type, params string[] names)
	{
		MemberInfo memberInfo = FindMember(type, names);
		if (!(memberInfo == null))
		{
			return GetValue(memberInfo, null);
		}
		return null;
	}

	public static bool WriteMember(object instance, object? value, params string[] names)
	{
		MemberInfo memberInfo = FindMember(instance.GetType(), names);
		if (memberInfo != null)
		{
			return SetValue(memberInfo, instance, value);
		}
		return false;
	}

	public static MemberInfo? FindMember(Type type, params string[] names)
	{
		foreach (string name in names)
		{
			MemberInfo memberInfo = DataMembers(type).FirstOrDefault((MemberInfo m) => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
			if (memberInfo != null)
			{
				return memberInfo;
			}
		}
		return null;
	}

	public static IEnumerable<object> EnumerateCollection(object? value)
	{
		if (value == null || value is string)
		{
			yield break;
		}
		if (value is IDictionary dictionary)
		{
			foreach (object value2 in dictionary.Values)
			{
				if (value2 != null)
				{
					yield return value2;
				}
			}
		}
		else
		{
			if (!(value is IEnumerable enumerable))
			{
				yield break;
			}
			foreach (object item in enumerable)
			{
				if (item != null)
				{
					yield return item;
				}
			}
		}
	}

	public static int GetCollectionCount(object? value)
	{
		if (value == null || value is string)
		{
			return 0;
		}
		if (value is ICollection collection)
		{
			return collection.Count;
		}
		object obj = ReadMember(value, "Length", "Count");
		if (obj == null)
		{
			return 0;
		}
		try
		{
			return Convert.ToInt32(obj, CultureInfo.InvariantCulture);
		}
		catch
		{
			return 0;
		}
	}

	public static object? GetIndexedItem(object? collection, int index)
	{
		if (collection == null || index < 0)
		{
			return null;
		}
		try
		{
			if (collection is IList list)
			{
				return index < list.Count ? list[index] : null;
			}
			PropertyInfo propertyInfo = collection.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(delegate(PropertyInfo p)
			{
				ParameterInfo[] indexParameters = p.GetIndexParameters();
				return indexParameters.Length == 1 && indexParameters[0].ParameterType == typeof(int) && p.GetMethod != null;
			});
			if (propertyInfo != null)
			{
				return propertyInfo.GetValue(collection, new object[1] { index });
			}
			MethodInfo methodInfo = collection.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(delegate(MethodInfo method)
			{
				return string.Equals(method.Name, "get_Item", StringComparison.Ordinal) && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(int);
			});
			if (methodInfo != null)
			{
				return methodInfo.Invoke(collection, new object[1] { index });
			}
		}
		catch (Exception ex)
		{
			Log.Warning($"GetIndexedItem failed for {SafeTypeName(collection)}[{index}]: {ex.Message}");
		}
		return null;
	}

	public static bool SetIndexedItem(object collection, int index, object item)
	{
		try
		{
			PropertyInfo propertyInfo = collection.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(delegate(PropertyInfo p)
			{
				ParameterInfo[] indexParameters = p.GetIndexParameters();
				return indexParameters.Length == 1 && indexParameters[0].ParameterType == typeof(int) && p.SetMethod != null;
			});
			if (propertyInfo != null)
			{
				propertyInfo.SetValue(collection, item, new object[1] { index });
				return true;
			}
			MethodInfo methodInfo = collection.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(delegate(MethodInfo m)
			{
				if (!string.Equals(m.Name, "set_Item", StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = m.GetParameters();
				return parameters.Length == 2 && parameters[0].ParameterType == typeof(int);
			});
			if (methodInfo != null)
			{
				methodInfo.Invoke(collection, new object[2] { index, item });
				return true;
			}
		}
		catch (Exception ex)
		{
			Log.Warning($"SetIndexedItem failed for {SafeTypeName(collection)}[{index}]: {ex.Message}");
		}
		return false;
	}

	public static object? CreateSameTypedCollection(object collection, int size)
	{
		ConstructorInfo[] constructors = collection.GetType().GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (ConstructorInfo constructorInfo in constructors)
		{
			ParameterInfo[] parameters = constructorInfo.GetParameters();
			if (parameters.Length != 1)
			{
				continue;
			}
			Type parameterType = parameters[0].ParameterType;
			if (!(parameterType != typeof(int)) || !(parameterType != typeof(long)))
			{
				try
				{
					object obj = ((parameterType == typeof(long)) ? ((object)(long)size) : ((object)size));
					return constructorInfo.Invoke(new object[1] { obj });
				}
				catch
				{
				}
			}
		}
		return null;
	}

	public static bool AddToCollection(object collection, object item, string? key = null)
	{
		object item2 = item;
		try
		{
			if (collection is IDictionary dictionary)
			{
				object key2 = key ?? ReadIdentity(item2) ?? item2.GetHashCode().ToString(CultureInfo.InvariantCulture);
				if (!dictionary.Contains(key2))
				{
					dictionary.Add(key2, item2);
					return true;
				}
				return false;
			}
			MethodInfo methodInfo = (from m in collection.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				where string.Equals(m.Name, "Add", StringComparison.OrdinalIgnoreCase)
				select m).FirstOrDefault(delegate(MethodInfo m)
			{
				ParameterInfo[] parameters = m.GetParameters();
				return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(item2);
			});
			if (methodInfo != null)
			{
				methodInfo.Invoke(collection, new object[1] { item2 });
				return true;
			}
		}
		catch (Exception ex)
		{
			Log.Warning("AddToCollection failed for " + SafeTypeName(collection) + ": " + ex.Message);
		}
		return false;
	}

	public static object? GetDictionaryValue(object dictionary, object key)
	{
		object key2 = key;
		if (dictionary is IDictionary dictionary2)
		{
			try
			{
				return dictionary2.Contains(key2) ? dictionary2[key2] : null;
			}
			catch (Exception ex)
			{
				Log.Warning($"GetDictionaryValue IDictionary lookup failed for {SafeTypeName(dictionary)}[{key2}]: {ex.Message}");
			}
		}
		try
		{
			PropertyInfo propertyInfo = dictionary.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(delegate(PropertyInfo p)
			{
				ParameterInfo[] indexParameters = p.GetIndexParameters();
				return indexParameters.Length == 1 && p.GetMethod != null && CanCoerce(key2, indexParameters[0].ParameterType);
			});
			if (propertyInfo != null)
			{
				object obj = CoerceValue(key2, propertyInfo.GetIndexParameters()[0].ParameterType);
				return propertyInfo.GetValue(dictionary, new object[1] { obj });
			}
		}
		catch (Exception ex2)
		{
			Log.Warning($"GetDictionaryValue indexer lookup failed for {SafeTypeName(dictionary)}[{key2}]: {ex2.Message}");
		}
		try
		{
			MethodInfo methodInfo = dictionary.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(delegate(MethodInfo m)
			{
				if (!string.Equals(m.Name, "get_Item", StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = m.GetParameters();
				return parameters.Length == 1 && CanCoerce(key2, parameters[0].ParameterType);
			});
			if (methodInfo != null)
			{
				object obj2 = CoerceValue(key2, methodInfo.GetParameters()[0].ParameterType);
				return methodInfo.Invoke(dictionary, new object[1] { obj2 });
			}
		}
		catch (Exception ex3)
		{
			Log.Warning($"GetDictionaryValue get_Item lookup failed for {SafeTypeName(dictionary)}[{key2}]: {ex3.Message}");
		}
		foreach (object item in EnumerateCollection(dictionary))
		{
			object obj3 = ReadMember(item, "Key", "key");
			if (obj3 != null && ValuesEqualLoose(obj3, key2))
			{
				return ReadMember(item, "Value", "value");
			}
		}
		return null;
	}

	private static bool ValuesEqualLoose(object left, object right)
	{
		if (object.Equals(left, right))
		{
			return true;
		}
		try
		{
			object objB = CoerceValue(right, left.GetType());
			if (object.Equals(left, objB))
			{
				return true;
			}
		}
		catch
		{
		}
		try
		{
			return object.Equals(CoerceValue(left, right.GetType()), right);
		}
		catch
		{
			return string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);
		}
	}

	public static string? ReadIdentity(object instance)
	{
		return ReadMember(instance, "itemID", "ItemID", "id", "Id", "name", "Name")?.ToString();
	}

	public static string DescribeObject(object? value)
	{
		if (value == null)
		{
			return "<null>";
		}
		string value2 = SafeTypeName(value);
		string text = ReadIdentity(value);
		string text2 = ReadDisplayName(value);
		return $"{value2} id='{text ?? ""}' name='{text2 ?? ""}'";
	}

	public static string? ReadDisplayName(object instance)
	{
		object obj = ReadMember(instance, "itemDisplayName", "displayName", "DisplayName", "itemName", "ItemName", "txtName", "name", "Name");
		if (!string.IsNullOrWhiteSpace(obj?.ToString()))
		{
			return obj.ToString();
		}
		object obj2 = ReadMember(instance, "shopItemSO", "ShopItemSO", "shopItemSo", "itemSO", "ItemSO");
		if (obj2 != null && obj2 != instance)
		{
			return ReadDisplayName(obj2);
		}
		return null;
	}

	public static object? InstantiateUnityObject(object source)
	{
		if (IsUnityObject(source))
		{
			Type objectType = source.GetType();
			object obj = (from m in (FindUnityType("Object") ?? objectType).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				where string.Equals(m.Name, "Instantiate", StringComparison.Ordinal)
				select m).FirstOrDefault(delegate(MethodInfo m)
			{
				ParameterInfo[] parameters = m.GetParameters();
				return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(objectType);
			})?.Invoke(null, new object[1] { source });
			if (obj != null)
			{
				WriteUnityObjectName(obj, ReadUnityObjectName(source) + "_BackplaneBoostClone");
			}
			return obj;
		}
		return null;
	}

	public static object? TryCastIl2CppObject(object value, Type targetType)
	{
		if (targetType.IsInstanceOfType(value))
		{
			return value;
		}
		MethodInfo methodInfo = value.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo m) => string.Equals(m.Name, "TryCast", StringComparison.Ordinal) && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
		if (methodInfo == null)
		{
			return null;
		}
		try
		{
			return methodInfo.MakeGenericMethod(targetType).Invoke(value, Array.Empty<object>());
		}
		catch
		{
			return null;
		}
	}

	public static bool TrySetTransformParentLike(object clone, object source)
	{
		object obj = ReadMember(clone, "transform", "Transform");
		object obj2 = ReadMember(source, "transform", "Transform");
		object obj3 = ((obj2 == null) ? null : ReadMember(obj2, "parent", "Parent"));
		if (obj == null || obj3 == null)
		{
			return false;
		}
		MethodInfo methodInfo = obj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(delegate(MethodInfo m)
		{
			if (!string.Equals(m.Name, "SetParent", StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = m.GetParameters();
			return parameters.Length == 2 && parameters[1].ParameterType == typeof(bool);
		});
		if (methodInfo == null)
		{
			return false;
		}
		try
		{
			methodInfo.Invoke(obj, new object[2] { obj3, false });
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static void SetGameObjectActive(object unityObject, bool active)
	{
		object obj = ReadMember(unityObject, "gameObject", "GameObject");
		if (obj == null)
		{
			obj = unityObject;
		}
		MethodInfo methodInfo = obj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(delegate(MethodInfo m)
		{
			if (!string.Equals(m.Name, "SetActive", StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = m.GetParameters();
			return parameters.Length == 1 && parameters[0].ParameterType == typeof(bool);
		});
		try
		{
			methodInfo?.Invoke(obj, new object[1] { active });
		}
		catch
		{
		}
	}

	public static object? GetComponent(object unityObject, Type componentType)
	{
		object obj = ReadMember(unityObject, "gameObject", "GameObject");
		if (obj == null)
		{
			obj = unityObject;
		}
		Type type = FindUnityType("GameObject");
		if (type != null)
		{
			obj = TryCastIl2CppObject(obj, type) ?? obj;
		}
		try
		{
			object obj2 = InvokeMethodReturn(obj, "GetComponent", componentType.Name);
			object obj3 = ((obj2 == null) ? null : TryCastIl2CppObject(obj2, componentType));
			if (obj3 != null)
			{
				return obj3;
			}
			object obj4 = InvokeMethodReturn(obj, "GetComponentCount");
			int num = ((obj4 != null) ? Convert.ToInt32(obj4, CultureInfo.InvariantCulture) : 0);
			for (int i = 0; i < num; i++)
			{
				obj2 = InvokeMethodReturn(obj, "GetComponentAtIndex", i);
				obj3 = ((obj2 == null) ? null : TryCastIl2CppObject(obj2, componentType));
				if (obj3 != null)
				{
					return obj3;
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warning($"GetComponent({componentType.Name}) failed on {SafeTypeName(obj)}: {ex.Message}");
		}
		return null;
	}

	public static IEnumerable<object> GetComponentsInChildren(object unityObject, Type componentType)
	{
		object obj = ReadMember(unityObject, "gameObject", "GameObject");
		if (obj == null)
		{
			obj = unityObject;
		}
		Type type = FindUnityType("GameObject");
		if (type != null)
		{
			obj = TryCastIl2CppObject(obj, type) ?? obj;
		}
		MethodInfo methodInfo = obj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo m) => string.Equals(m.Name, "GetComponentsInChildren", StringComparison.Ordinal) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(bool));
		if (methodInfo == null)
		{
			yield break;
		}
		object value;
		try
		{
			value = methodInfo.MakeGenericMethod(componentType).Invoke(obj, new object[1] { true });
		}
		catch (Exception ex)
		{
			Log.Warning($"GetComponentsInChildren({componentType.Name}) failed on {SafeTypeName(obj)}: {ex.Message}");
			yield break;
		}
		foreach (object item in EnumerateCollection(value))
		{
			object obj2 = TryCastIl2CppObject(item, componentType) ?? item;
			if (componentType.IsInstanceOfType(obj2))
			{
				yield return obj2;
			}
		}
	}

	public static string ReadTmpText(object textComponent)
	{
		return ReadMember(textComponent, "text", "Text", "m_text")?.ToString() ?? string.Empty;
	}

	public static bool SetTmpText(object textComponent, string text)
	{
		return false | InvokeMethod(textComponent, "SetText", text) | WriteMember(textComponent, text, "text", "Text", "m_text") | InvokeMethod(textComponent, "ForceMeshUpdate", true, true);
	}

	public static bool InvokeMethod(object instance, string methodName, params object?[] args)
	{
		MethodInfo methodInfo = FindMethod(instance.GetType(), methodName, args);
		if (methodInfo == null)
		{
			return false;
		}
		try
		{
			methodInfo.Invoke(instance, args);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static object? InvokeMethodReturn(object instance, string methodName, params object?[] args)
	{
		MethodInfo methodInfo = FindMethod(instance.GetType(), methodName, args);
		if (methodInfo == null)
		{
			return null;
		}
		try
		{
			return methodInfo.Invoke(instance, args);
		}
		catch
		{
			return null;
		}
	}

	private static MethodInfo? FindMethod(Type type, string methodName, object?[] args)
	{
		string methodName2 = methodName;
		object?[] args2 = args;
		return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(delegate(MethodInfo m)
		{
			if (!string.Equals(m.Name, methodName2, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			ParameterInfo[] parameters = m.GetParameters();
			if (parameters.Length != args2.Length)
			{
				return false;
			}
			for (int i = 0; i < parameters.Length; i++)
			{
				if (args2[i] != null && !parameters[i].ParameterType.IsInstanceOfType(args2[i]))
				{
					return false;
				}
			}
			return true;
		});
	}

	public static bool IsUnityObject(object value)
	{
		Type type = FindUnityType("Object");
		if (type != null)
		{
			return type.IsInstanceOfType(value);
		}
		return false;
	}

	public static Type? FindUnityType(string shortName)
	{
		string fullName = "UnityEngine." + shortName;
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			Type type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
			if (type != null)
			{
				return type;
			}
			Type type2 = SafeGetTypes(assembly).FirstOrDefault((Type t) => string.Equals(t.FullName, fullName, StringComparison.Ordinal));
			if (type2 != null)
			{
				return type2;
			}
		}
		return null;
	}

	public static string ReadUnityObjectName(object value)
	{
		return ReadMember(value, "name", "Name")?.ToString() ?? value.GetType().Name;
	}

	public static void WriteUnityObjectName(object value, string name)
	{
		WriteMember(value, name, "name", "Name");
	}

	public static object? CoerceValue(object? value, Type targetType)
	{
		if (value == null)
		{
			if (!targetType.IsValueType)
			{
				return null;
			}
			return Activator.CreateInstance(targetType);
		}
		Type type = value.GetType();
		if (targetType.IsAssignableFrom(type))
		{
			return value;
		}
		Type type2 = Nullable.GetUnderlyingType(targetType) ?? targetType;
		if (type2.IsEnum)
		{
			if (value is string value2)
			{
				return Enum.Parse(type2, value2, ignoreCase: true);
			}
			return Enum.ToObject(type2, Convert.ToInt32(value, CultureInfo.InvariantCulture));
		}
		if (type2 == typeof(string))
		{
			return value.ToString();
		}
		if (type2 == typeof(int))
		{
			return Convert.ToInt32(value, CultureInfo.InvariantCulture);
		}
		if (type2 == typeof(float))
		{
			return Convert.ToSingle(value, CultureInfo.InvariantCulture);
		}
		if (type2 == typeof(double))
		{
			return Convert.ToDouble(value, CultureInfo.InvariantCulture);
		}
		if (type2 == typeof(bool))
		{
			return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
		}
		return Convert.ChangeType(value, type2, CultureInfo.InvariantCulture);
	}

	private static bool CanCoerce(object? value, Type targetType)
	{
		if (value == null)
		{
			if (targetType.IsValueType)
			{
				return Nullable.GetUnderlyingType(targetType) != null;
			}
			return true;
		}
		Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
		Type type2 = value.GetType();
		if (!type.IsAssignableFrom(type2) && !type.IsEnum && !(type == typeof(string)) && !(type == typeof(int)) && !(type == typeof(float)) && !(type == typeof(double)))
		{
			return type == typeof(bool);
		}
		return true;
	}

	public static string SafeTypeName(object value)
	{
		return value.GetType().FullName ?? value.GetType().Name;
	}

	public static string Normalize(string value)
	{
		return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
	}
}

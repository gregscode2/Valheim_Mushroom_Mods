using System.Collections.Generic;
using UnityEngine;

public class ObjectDB : MonoBehaviour
{
	private static ObjectDB m_instance;

	public List<StatusEffect> m_StatusEffects = new List<StatusEffect>();

	public List<GameObject> m_items = new List<GameObject>();

	public List<Recipe> m_recipes = new List<Recipe>();

	private Dictionary<int, GameObject> m_itemByHash = new Dictionary<int, GameObject>();

	private Dictionary<ItemDrop.ItemData.SharedData, GameObject> m_itemByData = new Dictionary<ItemDrop.ItemData.SharedData, GameObject>();

	public static ObjectDB instance => m_instance;

	private void Awake()
	{
		m_instance = this;
		UpdateRegisters();
	}

	public void CopyOtherDB(ObjectDB other)
	{
		m_items = other.m_items;
		m_recipes = other.m_recipes;
		m_StatusEffects = other.m_StatusEffects;
		UpdateRegisters();
	}

	private void UpdateRegisters()
	{
		m_itemByHash.Clear();
		m_itemByData.Clear();
		foreach (GameObject item in m_items)
		{
			m_itemByHash.Add(item.name.GetStableHashCode(), item);
			ItemDrop component = item.GetComponent<ItemDrop>();
			if ((object)component != null)
			{
				m_itemByData[component.m_itemData.m_shared] = item;
			}
		}
	}

	public StatusEffect GetStatusEffect(int nameHash)
	{
		foreach (StatusEffect statusEffect in m_StatusEffects)
		{
			if (statusEffect.NameHash() == nameHash)
			{
				return statusEffect;
			}
		}
		return null;
	}

	public GameObject GetItemPrefab(string name)
	{
		return GetItemPrefab(name.GetStableHashCode());
	}

	public GameObject GetItemPrefab(int hash)
	{
		if (TryGetItemPrefab(hash, out var prefab))
		{
			return prefab;
		}
		return null;
	}

	public GameObject GetItemPrefab(ItemDrop.ItemData.SharedData sharedData)
	{
		if (TryGetItemPrefab(sharedData, out var prefab))
		{
			return prefab;
		}
		return null;
	}

	public bool TryGetItemPrefab(string name, out GameObject prefab)
	{
		return TryGetItemPrefab(name.GetStableHashCode(), out prefab);
	}

	public bool TryGetItemPrefab(int hash, out GameObject prefab)
	{
		return m_itemByHash.TryGetValue(hash, out prefab);
	}

	public bool TryGetItemPrefab(ItemDrop.ItemData.SharedData sharedData, out GameObject prefab)
	{
		return m_itemByData.TryGetValue(sharedData, out prefab);
	}

	public int GetPrefabHash(GameObject prefab)
	{
		return prefab.name.GetStableHashCode();
	}

	public List<ItemDrop> GetAllItems(ItemDrop.ItemData.ItemType type, string startWith)
	{
		List<ItemDrop> list = new List<ItemDrop>();
		foreach (GameObject item in m_items)
		{
			ItemDrop component = item.GetComponent<ItemDrop>();
			if (component.m_itemData.m_shared.m_itemType == type && component.gameObject.name.CustomStartsWith(startWith))
			{
				list.Add(component);
			}
		}
		return list;
	}

	public Recipe GetRecipe(ItemDrop.ItemData item)
	{
		foreach (Recipe recipe in m_recipes)
		{
			if (!(recipe.m_item == null) && recipe.m_item.m_itemData.m_shared.m_name == item.m_shared.m_name)
			{
				return recipe;
			}
		}
		return null;
	}
}

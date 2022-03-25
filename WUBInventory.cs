using UnityEngine;
using System;
namespace MBS
{
    public class WUBInventory : MBSSingleton<WUBInventory>
    {
        /// <summary>
        /// Direct access to the data and all the search and filter functions available to you
        /// The new methods provided in this class should be all you need but it is made public
        /// so you have the option of doing advanced work if you so please
        /// </summary>
        static public CML Inventory { get; set; } = new CML();
        /// <summary>
        /// Default filename to store a player's inventory to and load it from.
        /// This is guaranteed to be unique per player if loading and saving takes place
        /// after login and before logout. 
        /// Change this before calling Load or Save functions if you want to use something else
        /// </summary>
        static public string SaveFileName => WULogin.Username + "Inventory";
        /// <summary>
        /// When subtracting inventory and meta values, should it be allowed to go below zero?
        /// </summary>
        const bool AllowNegativeValues = false;
        const string OnlineSaveFieldName = "Inventory";

        private void Awake()
        {
            WULogin.OnLoggedIn += OnLoggedIn;
        }
        void OnLoggedIn(CML _) => LoadInventory();

        void LoadInventory() => Inventory.Load(SaveFileName);
        void SaveInventory() => Inventory.Save(SaveFileName);

#if WUDATA
        /// <summary>
        /// If Bridge:Data extension is present in the project you can save
        /// your entire inventory online rather than locally
        /// </summary>
        /// <param name="OnSuccess">(Optional) A function to trigger once upload has completed</param>
        /// <param name="OnFailed">(Optional) A function to trigger if uploading the data failed for some reason</param>
        void SaveInventoryOnline(Action<CML> OnSuccess = null, Action<CMLData> OnFailed = null)
        {
            var data = Encoder.Base64Encode(Inventory.ToString(false));
            CMLData fields = new CMLData();
            fields.Set(OnlineSaveFieldName, data);
            WUData.UpdateCategory(SaveFileName, fields, response: OnSuccess, errorResponse: OnFailed);
        }
        /// <summary>
        /// If Bridge:Data extension is present in the project you can load
        /// your entire inventory back after saving it online. 
        /// Default behaviour on fail is to load the locally saved game
        /// </summary>
        /// <param name="OnFail">(Optional) A function to trigger if uploading the data failed for some reason</param>
        void LoadInventoryFromOnline(Action<CMLData> OnFail = null) => WUData.FetchField(OnlineSaveFieldName, SaveFileName, ParseResponse, errorResponse: OnFail ?? HandleErrorWhileFetching);

        /// <summary>
        /// Upon successfully fetching the save game info from online it will be decoded
        /// and restored back into Inventory
        /// </summary>
        /// <param name="response">The response received from the server</param>
        void ParseResponse(CML response)
        {
            var results = string.Empty;
            if (WUData.WUDataPro)
                results = response.GetFirstNodeOfType(SaveFileName).String(OnlineSaveFieldName);
            else
                results = response[1].String(OnlineSaveFieldName);
            results = Encoder.Base64Decode(results);
            Inventory = new CML();
            Inventory.Parse(results);
        }

        /// <summary>
        /// Default behaviour when fetching data from online failed is to load the local game if present
        /// </summary>
        /// <param name="response"></param>
        void HandleErrorWhileFetching(CMLData response)
        {
            Inventory = new CML();
            LoadInventory();
        }
#endif
        #region Math on inventory items
        /// <summary>
        /// Add to the inventory item's value property. Create item if needed
        /// </summary>
        /// <param name="name">Item to add to inventory</param>
        /// <param name="qty">Quantity to add</param>
        /// <param name="meta">Optional additional info (Example: HP, Price, Rarity etc)</param>
        static public void AddItems(string name, long qty, CMLData meta = null)
        {
            if (qty <= 0) 
            {
                Debug.LogError("To deplete quantities, please use RemoveItems");
                return;
            }
            var item = Inventory.GetFirstNodeOfType(name);
            if (null == item)
            {
                Inventory.AddNode(name);
                item = Inventory.Last;
            }
            item.Addl(qty, "value");
            if (null != meta)
            {
                //remove any reference to "value" that might be set in the meta
                meta.Remove();
                //copy all the meta info over to the item
                foreach (var kvp in meta.defined)                
                    item.Set(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Subtract items from the inventory
        /// </summary>
        /// <param name="name">Inventory item to subtract</param>
        /// <param name="qty">How many to subtract</param>
        /// <returns>NULL if <e>qty</e> <= 0, FALSE if negative and negative values are not allowed. TRUE upon successful subtraction </returns>
        static public bool? RemoveItems(string name, long qty)
        {
            if (qty <= 0)
            {
                Debug.LogError("To add quantities, please use AddItems");
                return null;
            }
            var item = Inventory.GetFirstNodeOfType(name);
            if (null == item)
                return null;

            long newval = item.Long() - qty;
            if (newval < 0 && !AllowNegativeValues)
                return false;

            if (!AllowNegativeValues && newval == 0)
                Inventory.RemoveNode(item.ID);
            else
                item.Setl("value", newval);
            return true;
        }
        #endregion

        #region Math on meta values
        /// <summary>
        /// Add to the value of a meta field
        /// </summary>
        /// <param name="item_name">Item that has the meta to update (Ex: Longsword)</param>
        /// <param name="meta_field">The meta field to update (Ex: ATT)</param>
        /// <param name="qty">Amount to add to it's value</param>
        /// <returns>NULL if the item doesn't exist or QTY <= 0.
        /// Returns the new value on success</returns>
        static public long? MetaMathAdd(string item_name, string meta_field, long qty)
        {
            if (qty <= 0)
            {
                Debug.LogError("To deplete quantities, please use MetaMathSubtract");
                return null;
            }
            var item = Inventory.GetFirstNodeOfType(item_name);
            if (null == item)
            {
                Debug.LogError("Item not found");
                return null;
            }
            long result = item.Long(meta_field) + qty;
            item.Setl(meta_field, result);
            return result;
        }

        /// <summary>
        /// Subtract from the value of a meta field
        /// </summary>
        /// <param name="item_name">Item that has the meta to update (Ex: Longsword)</param>
        /// <param name="meta_field">The meta field to update (Ex: ATT)</param>
        /// <param name="qty">Amount to subtract from it's value</param>
        /// <param name="negative_to_zero">(Optional) If the result will be negative and invalid, force it to zero instead</param>
        /// <returns>NULL if the item doesn't exist or QTY <= 0 or if negative values are not allowed
        /// Returns the new value on success</returns>
        static public long? MetaMathSubtract(string item_name, string meta_field, long qty, bool negative_to_zero = false)
        {
            if (qty <= 0)
            {
                Debug.LogError("To increase quantities, please use MetaMathAdd");
                return null;
            }
            var item = Inventory.GetFirstNodeOfType(item_name);
            if (null == item)
            {
                Debug.LogError("Item not found");
                return null;
            }
            long result = item.Long(meta_field) - qty;
            if (result < 0 && !AllowNegativeValues)
            {
                if (negative_to_zero)
                {
                    result = 0;
                }
                else
                {
                    Debug.LogError("Negative values are not allowed");
                    return null;
                }
            }
            item.Setl(meta_field, result);
            return result;
        }
        #endregion

        #region Set meta values for an inventory item
        /// <summary>
        /// Set or create multiple meta fields and values for a specific inventory item
        /// </summary>
        /// <param name="item_name">The inventory item to update</param>
        /// <param name="meta_fields">A list of fields and their values passed via a single CMLData object</param>
        /// <returns>FALSE if item not found. True otherwise</returns>
        static public bool SetMetaFields(string item_name, CMLData meta_fields)
        {
            var item = Inventory.GetFirstNodeOfType(item_name);
            if (null == item)
            {
                Debug.LogError("Item not found");
                return false;
            }
            foreach(var kvp in meta_fields.defined)
                if (kvp.Key.ToLower() != "id")
                    item.Set(kvp.Key, kvp.Value);
            return true;
        }

        /// <summary>
        /// Set or create a single meta field and value for a specific inventory item
        /// </summary>
        /// <param name="item_name">The inventory item to update</param>
        /// <param name="meta_field">The meta field to create or set</param>
        /// <param name="meta_value">The meta field's raw value. Can be a string or number</param>
        /// <returns>FALSE if item not found. True otherwise</returns>
        static public bool SetMetaField(string item_name, string meta_field, string meta_value)
        {
            CMLData data = new CMLData();
            data.Set(meta_field, meta_value);
            return SetMetaFields(item_name, data);
        }
        #endregion

        #region Test Inventory quantity
        static public bool HasAtLeast(string name, int qty)
        {
            var indexer = Inventory.GetFirstNodeOfType(name);
            if (null == indexer)
                return qty == 0;
            return indexer.Int() > qty;
        }

        static public bool DoesNotHave(string name, int qty)
        {
            var indexer = Inventory.GetFirstNodeOfType(name);
            if (null == indexer)
                return true;
            return indexer.Int() < qty;
        }

        static public bool HasExactly(string name, int qty)
        {
            var indexer = Inventory.GetFirstNodeOfType(name);
            if (null == indexer)
                return qty == 0;
            return indexer.Int() == qty;
        }
        #endregion
    }
}
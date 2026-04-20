using System;
using System.Collections.Generic;
using UnityEngine;

namespace vanhaodev.savemanager.Samples.Inventory
{
	public class AccountManager : MonoBehaviour
	{
		private const string AccountFileName = "account";
		public AccountModel Account { get; private set; }

		private void Awake()
		{
			//set AES key before save/load
			SaveEncryption.SetKey(EncryptionType.AES, "aeskey_12345678**..hardcoded"); //any string you want
		}

		public void LoadData()
		{
			Account = Save.Get<AccountModel>(AccountFileName);
			
			if (Account == null)
			{
				Account = new AccountModel
				{
					Password = "password123123"
				};
			}
		}

		public void SaveData()
		{
			if (Account == null)
			{
				Account = new AccountModel
				{
					Password = "password123123"
				};
			}
			
			//using AES
			Save.Set(AccountFileName, Account, EncryptionType.AES);
		}
	}
}
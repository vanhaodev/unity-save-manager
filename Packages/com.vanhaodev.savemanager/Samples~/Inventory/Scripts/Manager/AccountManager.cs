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
			// Set the secret before ANY save/load: used for AES and for signing saves (detects edited files).
			// Any string works. Same string on every device = saves can be shared;
			// per-player string (e.g. account id) = a save only loads for its owner.
			SaveEncryption.SetKey("aeskey_12345678**..hardcoded");
		}

		public void LoadData()
		{
			// Require AES: a file switched to "no encryption" is rejected
			Account = Save.Get<AccountModel>(AccountFileName, EncryptionType.AES);
			
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
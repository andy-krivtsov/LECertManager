# Let's Encrypt Certificate Manager

### Описание проекта

**Let's Encrypt Certificate Manager (LECertManager)** предназначен для управления сертификатами, 
получаемыми с помощью Lets Encrypt CA (http://letsencrypt.org/) для различных Azure-сервисов.

LECertManager позволяет получить и поддерживать в актуальном состоянии сертификаты, размещаемые в Azure Key Vault. 
Остальные сервисы (Azure App Service, AKS и т.д.) должны импортировать сертификат из KeyVault тем или иным способом,
и обновлять его при обновлении версии в Key Vault.

Общая схема работы LECertManager:
1. В конфигруации LECertManager создается раздел, описывающий сертификат (домены, Key Vault для хранения, доступ к DNS для подтверждения принадлежности и т.д.)
2. После этого вызывается функция `UpdateCertificate`, которая запрашивает новый сертификат из LE CA, если его нет в Key Vault
3. В дальнейшем вызываемая по таймеру раз в сутки функция `UpdateAllCertificates` проверяет все сконфигурированные сертификаты (сертификат должен быть отмечен как `autoUpdate == true`) 
и обновляет их мере устаревания (сертификат обновляется за `RenewalBeforeExpireDays` дней до истечения, парметр из конфигурации)
4. Удаление сертификата выполняется вручную - из всех сервисов, которые его используют, из KeyVault и из конфигурации LECertManager

### Подтверждение владения доменом

При запросе (обновлении) сертификата от LE требуется подтвердить владение доменом или доменами, если указано несоклько. 

LECertManager сейчас поддерживает только подтверждние через DNS (Challenge DNS-1 https://letsencrypt.org/docs/challenge-types/).
В этой процедуре необходимо при запросе создать TXT запись в DNS (__acme-challenge.<YOUR_DOMAIN>_), содержимым которой будет 
вычисляемое каждый раз значение.

LECertManager на данный момент поддерживает автоматическое создание таких записей только в Azure DNS (теоретически возможно добавление плагинов для других DNS-провайдеров)
 
### Управление Lets Encrypt Account

Все операции по управлению сертифкатами LE выполняются в рамках так называемых _Account'ов_, которые фактически представлюят собой 
приватный ключ и некоторую метаинформацию. Аккаунты сохраняются на серверах LE в течении 30 дней и есть ограничения на количество
создаваемых за сутки новых аккаунтов.

LECertManager в целом не хранит Account и создает новые для каждого запроса сертификата, но может кешировать ключ аккаунта в KeyVault 
и обновлять его каждые `cacheTimeHours` часов (по умолчанию - 48 часов). Это уменьшает количество созданий аккаунтов в
 течении суток для одной инсталяции LECertManager.
  
### Архитектура

LECertManager представляет собой _Azure Functions_ приложение.
Состоит из:
1. Нескольких Azure Functions (класс `LECertificateManager`), являющихся только front-end'ом для внутренних сервисов
2. Набора сервисов (Namespace `LECertManager.Services`), реализующих весь функционал приложения

##### Список Azure Functions
| Function Name       | C# function   | API URL     | Назначение
| :------------------ | :------------ | :---------- | :----------
| GetCertificateInfo    | GetCertificateInfoAsync | GET /api/certificates/{name}       | Получить данные о сертификате
| CheckCertificate      | CheckCertificateAsync   | GET api/certificates/{name}/status | Проверить не истек ли сертификат
| UpdateCertificate     | UpdateCertificateAsync  | POST api/certificates/{name}       | Обновить сертификат (&force=true - принудительно)
| UpdateAllCertificates | UpdateAllCertificates   | timer " 0 0 1 * * * "              | Регулярная проверка и обновление сертификатов

### Конфигурация приложения
Приложение загружает конфигурацию из переменных окружения и файла конфигурации _appsettings.json_. В отличие от стандартного приложения ASP.Net Core,
 файл _appsettings.json_ должен находиться в Azure Storage Blob, путь к которому указывают три переменных окружения
(для Azure Funcions они задаются через Application Configuration)

* **ConfigStorageUri**  (URI Storage-аккаунта)
* **ConfigStorageSas**  (SAS для доступа к storage) или **ConfigStorageKey**  (Key для доступа к storage)
* **ConfigStorageContainer**  (имя BLOB конейтейнера)

Если хотя бы одна из этих переменных не задана, то загружается файл _appsettings.local.json_ (опция для локального запуска при разработке)

### Развертывание приложения 
Приложение пока полагается только на использование _Azure Managed Service Identities (MSI)_ для доступа к Key Vault и доступа к DNS записям (для Azure DNS), поэтому должно быть развернуто
в том же тенанте, где находится Key Vault для хранения сертификатов и объекты DNS-зон для домена

#### Процедура установки (с помощью шаблона)
Ниже процедура установки с использование ARM-Template'а. 
В этом случае создается отдельный KeyVault для менеджера. При необходимости использовать существующий необходимо удалить созданный, указать существующий в файле конфигурации и создать политику доступа к MSI приложния 
1. Создать ресурсную группу (напрмер LECertManager)
2. Выполнить развертывание менеджера из ARM-Template. Необходимо указать нужны  namePrefix в файле с параметрами шаблона
PowerShell команда:
`New-AzResourceGroupDeployment -Name LE-Mgr-Deploy01 -ResourceGroupName LECertManager -TemplateParameterFile .\le-cert-mgr-params.json -TemplateFile .\le-cert-mgr-template.json -Verbose`
3. Создать файл конфигурации (пример appsettings.default.json) с именем appsettings.json, и положить его в созаднный Storage Account, в контейнер app-config (заменив созданный пустой) 
4. Добавить политику для доступа к KeyVault для администратора 
5. Добавить доступ для MSI приложения к объектам DNS-зон нужных доменов
 

#### Процедура установки (ручная)

1. Создать Azure Key Vault (если его еще нет)
2. Создать Azure Function App в тенанте, где размещаются Key Vault (при этом будет создан Azure Storage Account)
3. Настроить через Azure DevOps (или другой CI/CD) публикацию из репозитория приложения в этот Application.
   При настройке публикции в продакшен нужно устанавливать обновления из ветки Master
   https://github.com/andy-krivtsov/LECertManager.git
4. Сделать в созданном Storage Accout контейнер для размщеения конфигурации приложения
5. Создать файл конфигурации (см. пример ниже) и положить его в созданный контейнер. В файле конфигурации указать доступы к Key Vault и Azure DNS. 
6. Создать SAS-сигнатуру для чтения конфиуграции 
7. Задать в параметрах приложения три переменные для доступа к конфигурации ConfigStorageUri,ConfigStorageSas,ConfigStorageContainer
8. Включить в приложении поддержку System Managed Service Identities
9. Сделать в KeyVault политику, предоставляющую доступ к Secrets & Certificates для MSI приложения
10. Предоставить MSI приложения доступ на управление зонами DNS для доменов сертификата 
11. Попробовать запустить function UpdateCertificate с указанием имени сертификата для получения 

#### Пример файла конфигурации
```
{
    "appSettings": {       
        "acmeAccount": {
            "email": "admin@mechanus.ru"
        },        

        "certificates": [
            {
                "name": "TestCertificate1",                
                "domains": [ "az.mechanus.ru", "*.az.mechanus.ru" ],

                "acmeServer": "staging", 
                
                "autoUpdate": true, 
                
                "dnsChallenge": {
                    "provider": "AzureDns",
                    "azureDnsZone": {
                        "name": "az.mechanus.ru",
                        "subscriptionId": "xxxxxx-xxxx-xxx-xxx",
                        "resourceGroup": "LECertManager"
                    }                    
                },
                
                "keyVault":
                {
                    "uri": "https://lecertmgr-keyvault.vault.azure.net/"
                }
            }
        ],
        
        "renewalBeforeExpireDays": 20, 
        
        "acmeKeyCache": {
            "filePath": "%APPDATA%\\LECertManager\\Debug\\key-cache.txt",            
            "secretName": "acmeAccountKey",
            "cacheTimeHours": 48,            
            "keyVault":
            {
                "uri": "https://lecertmgr-keyvault.vault.azure.net/"
            }
        }
    }
}
```
param([string]$AdminExe=(Join-Path $PSScriptRoot '..\dist\Lethal company pack Updater Admin.exe'))
$ErrorActionPreference='Stop'
$asm=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($AdminExe))
$repo=$asm.GetType('LcPack.Channel').GetField('Repo').GetRawConstantValue()
$type=$asm.GetType('LcPack.Workspace');$root=$type.GetProperty('DefaultRoot').GetValue($null,$null)
Write-Host "仅配置 $repo 的本机发布权限。不要把密钥发到聊天。"
$secret=Read-Host '粘贴该仓库 Contents: Read and write 的 Fine-grained token' -AsSecureString
$pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
try{
 $token=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
 $headers=@{Authorization='Bearer '+$token;'User-Agent'='LcPack-Setup';'X-GitHub-Api-Version'='2022-11-28'}
 try{$user=Invoke-RestMethod 'https://api.github.com/user' -Headers $headers;$repository=Invoke-RestMethod "https://api.github.com/repos/$repo" -Headers $headers}catch{throw 'GitHub 验证失败，未保存。请检查网络和此仓库的授权。'}
 if($user.login -ne $repo.Split('/')[0] -or $repository.private -or $repository.archived -or -not $repository.permissions.push){throw '必须是此公开仓库所有者的可写凭据。'}
 $ws=[Activator]::CreateInstance($type,@($root));$ws.SaveToken($token)
 Write-Host '已在本机加密保存。'
}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer);$token=$null;$headers=$null;$secret.Dispose()}

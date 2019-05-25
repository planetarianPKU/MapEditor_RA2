# -*- coding: utf-8 -*-
#import struct
import base64
def save_to_file(file_name, contents):
    fh = open(file_name, 'w')
    fh.write(contents)
    fh.close()



#读取
file = open('xxxx.txt', 'r');
binstr = file.read();
#数据标准化处理，因为复制过来的数据是一行行的
alist=binstr.splitlines();
totalstr='';

for line in alist:
    totalstr=totalstr+line.split("=")[1];
save_to_file('strr.txt', totalstr)    
#totalstr=totalstr+'=';#文件末尾的补位符号会被切掉，因此要手动补一下

#解码得到byte array
#totalstr=bytes(totalstr,"utf8");#str转byte
a=base64.b64decode(totalstr);#解码
lista=list(a);#十进制数组

print(len(a))

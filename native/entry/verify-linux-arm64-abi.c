#include <dirent.h>
#include <stddef.h>
#include <stdio.h>
#include <sys/stat.h>

/* These are the offsets used by Scout's raw ARM64 Unix filesystem readers. */
_Static_assert(offsetof(struct stat, st_dev) == 0, "stat device offset");
_Static_assert(offsetof(struct stat, st_ino) == 8, "stat inode offset");
_Static_assert(offsetof(struct stat, st_mode) == 16, "stat mode offset");
_Static_assert(offsetof(struct stat, st_size) == 48, "stat length offset");
_Static_assert(sizeof(((struct stat *)0)->st_ino) == 8, "stat inode width");
_Static_assert(sizeof(struct stat) <= 512, "stat buffer size");
_Static_assert(offsetof(struct dirent, d_reclen) == 16, "dirent record length offset");
_Static_assert(offsetof(struct dirent, d_type) == 18, "dirent type offset");
_Static_assert(offsetof(struct dirent, d_name) == 19, "dirent name offset");

int main(void)
{
    printf("stat size=%zu dev=%zu ino=%zu mode=%zu size_offset=%zu\n",
        sizeof(struct stat), offsetof(struct stat, st_dev), offsetof(struct stat, st_ino),
        offsetof(struct stat, st_mode), offsetof(struct stat, st_size));
    printf("dirent size=%zu reclen=%zu type=%zu name=%zu\n",
        sizeof(struct dirent), offsetof(struct dirent, d_reclen),
        offsetof(struct dirent, d_type), offsetof(struct dirent, d_name));
    return 0;
}
